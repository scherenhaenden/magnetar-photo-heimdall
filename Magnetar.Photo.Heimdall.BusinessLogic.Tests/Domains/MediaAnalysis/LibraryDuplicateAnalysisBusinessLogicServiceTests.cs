using System.Collections.Concurrent;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;
using Xunit;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Tests.Domains.MediaAnalysis;

public sealed class LibraryDuplicateAnalysisBusinessLogicServiceTests
{
    private static readonly string[] RepeatedSizePaths = ["a.jpg", "b.jpg", "same-size-different.jpg"];
    private static readonly string[] ExpectedDuplicatePaths = ["a.jpg", "b.jpg"];

    [Fact]
    public async Task AnalyzeAsync_ReportsInvalidCatalogPathAndContinuesOtherCandidates()
    {
        var root = Path.Combine(SafeTemporaryDirectory, $"heimdall-path-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var bytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(Path.Combine(root, "a.jpg"), bytes);
            await File.WriteAllBytesAsync(Path.Combine(root, "b.jpg"), bytes);
            var fixedTime = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(root, "a.jpg"), fixedTime);
            File.SetLastWriteTimeUtc(Path.Combine(root, "b.jpg"), fixedTime);
            var library = new LibraryRootDataAccessModel(Guid.NewGuid(), "test", root, DateTimeOffset.UtcNow);
            var timestamp = new DateTimeOffset(fixedTime);
            var catalog = new FixtureCatalog(
            [
                new CataloguedAssetDataAccessModel(Guid.NewGuid(), library.Id, "../outside.jpg", bytes.Length, timestamp),
                new CataloguedAssetDataAccessModel(Guid.NewGuid(), library.Id, "a.jpg", bytes.Length, timestamp),
                new CataloguedAssetDataAccessModel(Guid.NewGuid(), library.Id, "b.jpg", bytes.Length, timestamp)
            ]);
            var service = new LibraryDuplicateAnalysisBusinessLogicService(catalog,
                new ExactDuplicateCandidateGrouperBusinessLogicService(new Blake3ContentHasherBusinessLogicService()));

            var result = await service.AnalyzeAsync(library, maximumConcurrency: 2);

            Assert.Equal(2, result.HashesComputed);
            Assert.Equal("../outside.jpg", Assert.Single(result.Issues).RelativePath);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_RevalidatesHashesOnSecondPassAndNeverMutatesFiles()
    {
        await using var context = await TestContext.CreateAsync();
        var duplicateBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var differentSameSizeBytes = new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 };
        var uniqueSizeBytes = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 };
        await File.WriteAllBytesAsync(Path.Combine(context.LibraryPath, "a.jpg"), duplicateBytes);
        await File.WriteAllBytesAsync(Path.Combine(context.LibraryPath, "b.jpg"), duplicateBytes);
        await File.WriteAllBytesAsync(Path.Combine(context.LibraryPath, "same-size-different.jpg"), differentSameSizeBytes);
        await File.WriteAllBytesAsync(Path.Combine(context.LibraryPath, "unique-size.jpg"), uniqueSizeBytes);
        var originalFiles = await CaptureFilesAsync(context.LibraryPath);

        var recorder = new RecordingContentHasher(new Blake3ContentHasherBusinessLogicService());
        var database = new SqliteLibraryCatalogDataAccessDatabaseService(context.DatabasePath);
        var catalog = new LibraryCatalogDataAccessService(database, new PhysicalMediaFileScannerDataAccessIoService());
        await catalog.InitializeAsync();
        var library = await catalog.GetOrCreateLibraryAsync("integration-test", context.LibraryPath);
        var service = new LibraryDuplicateAnalysisBusinessLogicService(
            catalog,
            new ExactDuplicateCandidateGrouperBusinessLogicService(recorder));

        var first = await service.AnalyzeAsync(library, maximumConcurrency: 2);

        Assert.Equal(4, first.CataloguedAssetCount);
        Assert.Equal(3, first.CandidateFileCount);
        Assert.Equal(3, first.HashesComputed);
        Assert.Equal(3, first.HashesPersisted);
        Assert.Equal(0, first.HashesReused);
        Assert.Empty(first.Issues);
        Assert.Equal(
            RepeatedSizePaths,
            recorder.HashedPaths.Order(StringComparer.Ordinal));
        var firstGroup = Assert.Single(first.Groups);
        Assert.Equal(duplicateBytes.LongLength, firstGroup.FileLength);
        Assert.Equal("BLAKE3", firstGroup.Algorithm);
        Assert.Equal(1, firstGroup.AlgorithmVersion);
        Assert.Equal(ExpectedDuplicatePaths, firstGroup.RelativePaths);

        var persistedAssets = await catalog.ListAssetsAsync(library.Id);
        Assert.All(persistedAssets.Where(asset => asset.RelativePath != "unique-size.jpg"), asset =>
        {
            Assert.Equal("BLAKE3", asset.HashAlgorithm);
            Assert.Equal(Blake3ContentHasherBusinessLogicService.FullHashVersion, asset.HashAlgorithmVersion);
            Assert.Matches("^[0-9a-f]{64}$", asset.FullHash!);
        });
        Assert.Null(Assert.Single(persistedAssets, asset => asset.RelativePath == "unique-size.jpg").FullHash);
        var storedGroup = Assert.Single(await catalog.ListConfirmedDuplicateGroupsAsync(
            library.Id,
            "BLAKE3",
            Blake3ContentHasherBusinessLogicService.FullHashVersion));
        Assert.Equal(ExpectedDuplicatePaths, storedGroup.Select(asset => asset.RelativePath));

        recorder.Clear();
        var second = await service.AnalyzeAsync(library, maximumConcurrency: 2);

        Assert.Equal(3, second.HashesComputed);
        Assert.Equal(0, second.HashesReused);
        Assert.Equal(3, second.HashesPersisted);
        Assert.Equal(
            RepeatedSizePaths,
            recorder.HashedPaths.Order(StringComparer.Ordinal));
        Assert.Equal(ExpectedDuplicatePaths, Assert.Single(second.Groups).RelativePaths);
        AssertFileSnapshotsEqual(originalFiles, await CaptureFilesAsync(context.LibraryPath));
    }

    private static async Task<SortedDictionary<string, FileSnapshot>> CaptureFilesAsync(string root)
    {
        var files = new SortedDictionary<string, FileSnapshot>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            var info = new FileInfo(path);
            files.Add(relativePath, new FileSnapshot(await File.ReadAllBytesAsync(path), info.LastWriteTimeUtc));
        }

        return files;
    }

    private static void AssertFileSnapshotsEqual(
        IReadOnlyDictionary<string, FileSnapshot> expected,
        IReadOnlyDictionary<string, FileSnapshot> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var (relativePath, expectedSnapshot) in expected)
        {
            var actualSnapshot = actual[relativePath];
            Assert.Equal(expectedSnapshot.LastWriteUtc, actualSnapshot.LastWriteUtc);
            Assert.Equal(expectedSnapshot.Contents, actualSnapshot.Contents);
        }
    }

    private sealed record FileSnapshot(byte[] Contents, DateTime LastWriteUtc);

    private static string SafeTemporaryDirectory => OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();

    private sealed class FixtureCatalog(IReadOnlyList<CataloguedAssetDataAccessModel> assets) : ILibraryCatalogDataAccessService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LibraryRootDataAccessModel> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryRootDataAccessModel> GetOrCreateLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ScanLibraryAsync(LibraryRootDataAccessModel library, CancellationToken cancellationToken = default) => Task.FromResult(assets.Count);
        public Task<bool> SaveHashEvidenceAsync(Guid id, string path, long length, DateTimeOffset lastWrite, string algorithm, int version, string hash, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> CountAssetsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(assets.Count);
        public Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListAssetsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(assets);
        public Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListRepeatedSizeAssetsAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CataloguedAssetDataAccessModel>>(assets.GroupBy(asset => asset.Length).Where(group => group.Count() > 1).SelectMany(group => group).ToArray());
        public Task<IReadOnlyList<long>> ListRepeatedSizeValuesAsync(Guid id, long? afterLength, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>(assets.GroupBy(asset => asset.Length).Where(group => group.Count() > 1).Select(group => group.Key).Where(length => afterLength is null || length > afterLength).Order().Take(limit).ToArray());
        public Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListAssetsBySizeAsync(Guid id, long length, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CataloguedAssetDataAccessModel>>(assets.Where(asset => asset.Length == length).ToArray());
        public Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListHashCandidatesBySizeAsync(Guid id, string algorithm, int version, CancellationToken cancellationToken = default) => Task.FromResult(assets);
        public Task<IReadOnlyList<IReadOnlyList<CataloguedAssetDataAccessModel>>> ListConfirmedDuplicateGroupsAsync(Guid id, string algorithm, int version, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IReadOnlyList<CataloguedAssetDataAccessModel>>>(Array.Empty<IReadOnlyList<CataloguedAssetDataAccessModel>>());
    }

    private sealed class RecordingContentHasher(IContentHasherBusinessLogicService inner) : IContentHasherBusinessLogicService
    {
        private readonly ConcurrentBag<string> _hashedPaths = [];
        public string[] HashedPaths => _hashedPaths.ToArray();

        public async ValueTask<HashEvidenceBusinessLogicModel> ComputeAsync(string filePath, CancellationToken ct = default)
        {
            _hashedPaths.Add(Path.GetFileName(filePath));
            return await inner.ComputeAsync(filePath, ct);
        }

        public void Clear()
        {
            while (_hashedPaths.TryTake(out _))
            {
            }
        }
    }

    private sealed class TestContext(string rootPath) : IAsyncDisposable
    {
        public string RootPath { get; } = rootPath;
        public string LibraryPath => Path.Combine(RootPath, "library");
        public string DatabasePath => Path.Combine(RootPath, "catalog.sqlite");

        public static Task<TestContext> CreateAsync()
        {
            var context = new TestContext(Path.Combine(SafeTemporaryDirectory, $"heimdall-business-logic-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(context.LibraryPath);
            return Task.FromResult(context);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnectionPoolCleanup.Clear();
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private static class SqliteConnectionPoolCleanup
    {
        public static void Clear() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
