using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;
using Xunit;

namespace Magnetar.Photo.Heimdall.Desktop.IntegrationTests;

/// <summary>
/// Real integration tests: no mocks, no fakes.
/// Uses a temporary directory on the real filesystem and a real SQLite database.
/// Exercises the full PresentationLogic → BusinessLogic → DataAccess → DataAccess.IO chain.
/// </summary>
public sealed class LibraryOnboardingIntegrationTests : IDisposable
{
    // Each test instance gets its own isolated temp directory.
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"heimdall-desktop-it-{Guid.NewGuid():N}");

    public LibraryOnboardingIntegrationTests() =>
        Directory.CreateDirectory(_testRoot);

    // ── Test 1 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAndScanAsync_catalogues_only_supported_media_files()
    {
        // Arrange: two supported media files + one text file that must be ignored.
        var nested = Directory.CreateDirectory(Path.Combine(_testRoot, "2026", "trip"));
        await File.WriteAllBytesAsync(Path.Combine(_testRoot, "camera.jpg"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "raw.NEF"), [4, 5, 6, 7]);
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "notes.txt"), "ignore me");

        var (catalog, facade) = BuildStack();

        // Act: add library and scan through the presentation layer.
        var result = await facade.AddAndScanAsync("Summer trip", _testRoot);

        // Assert – scan result reflects real filesystem.
        Assert.Equal(2, result.Scan.CataloguedAssetCount);
        Assert.Equal("Summer trip", result.Library.DisplayName);

        // Assert – real SQLite contains the correct assets.
        var assets = await catalog.ListAssetsAsync(result.Library.Id);
        Assert.Equal(2, assets.Count);
        Assert.Contains(assets, a => a.RelativePath == "camera.jpg");
        Assert.Contains(assets, a =>
            a.RelativePath == Path.Combine("2026", "trip", "raw.NEF"));
    }

    // ── Test 2 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_upserts_changed_file_without_duplicating_it()
    {
        // Arrange: single media file, first scan.
        await File.WriteAllBytesAsync(Path.Combine(_testRoot, "photo.jpg"), [0x01, 0x02, 0x03]);

        var (catalog, facade) = BuildStack();
        var firstResult = await facade.AddAndScanAsync("Upsert test", _testRoot);

        var assetsAfterFirst = await catalog.ListAssetsAsync(firstResult.Library.Id);
        Assert.Single(assetsAfterFirst);
        Assert.Equal(3L, assetsAfterFirst[0].Length);

        // Act: modify the file on disk and re-scan via the service directly.
        // (A second AddAndScanAsync would fail: the path UNIQUE constraint rejects it.)
        await File.WriteAllBytesAsync(Path.Combine(_testRoot, "photo.jpg"),
            [0x01, 0x02, 0x03, 0x04, 0x05]);

        var service = new LibraryScanService(catalog);
        var secondScan = await service.ScanAsync(firstResult.Library);

        // Assert: still exactly one record, with updated length.
        var assetsAfterSecond = await catalog.ListAssetsAsync(firstResult.Library.Id);
        Assert.Single(assetsAfterSecond);
        Assert.Equal(1, secondScan.CataloguedAssetCount);
        Assert.Equal(5L, assetsAfterSecond[0].Length);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the real object graph without any mocks.
    /// Returns both the catalog (for direct SQLite assertions) and the facade
    /// (the presentation-layer entry point).
    /// </summary>
    private (ILibraryCatalogDataAccessService catalog, LibraryOnboardingFacade facade) BuildStack()
    {
        var dbPath = Path.Combine(_testRoot, "catalog.db");
        var databaseService = new SqliteLibraryCatalogDatabaseService(dbPath);
        var catalog = new LibraryCatalogDataAccessService(
            databaseService,
            new PhysicalMediaFileScannerDataAccessIoService());
        var facade = new LibraryOnboardingFacade(
            new LibraryScanService(catalog));
        return (catalog, facade);
    }
}
