using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;
using Microsoft.Data.Sqlite;
using System.Globalization;
using Xunit;

namespace Magnetar.Photo.Heimdall.DataAccess.Database.Tests;

public sealed class LibraryCatalogHashPersistenceTests
{
    private static readonly long[] FirstRepeatedLength = [10L];
    private static readonly long[] SecondRepeatedLength = [20L];
    private static readonly string[] FirstSizePaths = ["a", "b"];
    [Fact]
    public async Task HashEvidenceIsPersistedAndInvalidatedWhenFileIdentityChanges()
    {
        await using var context = await TestContext.CreateAsync();
        var library = await context.Database.AddLibraryAsync("test", "/photos");
        var timestamp = DateTimeOffset.Parse("2026-01-02T03:04:05Z", CultureInfo.InvariantCulture);
        await context.Database.UpsertAssetAsync(library.Id, "a.jpg", 42, timestamp);
        await context.Database.UpsertAssetAsync(library.Id, "b.jpg", 42, timestamp);

        Assert.True(await context.Database.SaveHashEvidenceAsync(library.Id, "a.jpg", 42, timestamp, "BLAKE3", 1, HashA));
        Assert.False(await context.Database.SaveHashEvidenceAsync(library.Id, "a.jpg", 43, timestamp, "BLAKE3", 1, HashB));
        var saved = Assert.Single(await context.Database.ListAssetsAsync(library.Id), asset => asset.RelativePath == "a.jpg");
        Assert.Equal("blake3", saved.FullHash is null ? null : saved.HashAlgorithm?.ToLowerInvariant());
        Assert.Equal(1, saved.HashAlgorithmVersion);
        Assert.Equal(HashA, saved.FullHash);

        await context.Database.UpsertAssetAsync(library.Id, "a.jpg", 42, timestamp.AddSeconds(1));
        var refreshed = Assert.Single(await context.Database.ListAssetsAsync(library.Id), asset => asset.RelativePath == "a.jpg");
        Assert.Null(refreshed.FullHash);
        Assert.Null(refreshed.HashAlgorithm);
        Assert.Null(refreshed.HashAlgorithmVersion);
    }

    [Fact]
    public async Task CandidatesAreLimitedToUnhashedAssetsInRepeatedSizeBuckets()
    {
        await using var context = await TestContext.CreateAsync();
        var library = await context.Database.AddLibraryAsync("test", "/photos");
        var timestamp = DateTimeOffset.UtcNow;
        await context.Database.UpsertAssetAsync(library.Id, "a.jpg", 10, timestamp);
        await context.Database.UpsertAssetAsync(library.Id, "b.jpg", 10, timestamp);
        await context.Database.UpsertAssetAsync(library.Id, "unique.jpg", 99, timestamp);
        await context.Database.SaveHashEvidenceAsync(library.Id, "a.jpg", 10, timestamp, "BLAKE3", 1, HashA);

        var candidates = await context.Database.ListHashCandidatesBySizeAsync(library.Id, "BLAKE3", 1);
        Assert.Equal(ExpectedCandidatePaths, candidates.Select(asset => asset.RelativePath));
    }

    [Fact]
    public async Task RepeatedSizeQuery_FiltersUniqueFilesInSqlAndCountsAllCataloguedAssets()
    {
        await using var context = await TestContext.CreateAsync();
        var library = await context.Database.AddLibraryAsync("test", "/photos");
        var timestamp = DateTimeOffset.UtcNow;
        await context.Database.UpsertAssetAsync(library.Id, "unique.jpg", 101, timestamp);
        await context.Database.UpsertAssetAsync(library.Id, "same-a.jpg", 42, timestamp);
        await context.Database.UpsertAssetAsync(library.Id, "same-b.jpg", 42, timestamp);

        Assert.Equal(3, await context.Database.CountAssetsAsync(library.Id));
        var candidates = await context.Database.ListRepeatedSizeAssetsAsync(library.Id);
        Assert.Equal(ExpectedRepeatedCandidatePaths, candidates.Select(asset => asset.RelativePath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RepeatedSizeValuesPageAndAssetsBySizeKeepBucketsWhole()
    {
        await using var context = await TestContext.CreateAsync();
        var library = await context.Database.AddLibraryAsync("test", "/photos");
        var timestamp = DateTimeOffset.UtcNow;
        foreach (var entry in new[] { ("a", 10L), ("b", 10L), ("c", 20L), ("d", 20L), ("unique", 30L) })
            await context.Database.UpsertAssetAsync(library.Id, entry.Item1, entry.Item2, timestamp);

        Assert.Equal(FirstRepeatedLength, await context.Database.ListRepeatedSizeValuesAsync(library.Id, afterLength: null, limit: 1));
        Assert.Equal(SecondRepeatedLength, await context.Database.ListRepeatedSizeValuesAsync(library.Id, afterLength: 10, limit: 1));
        Assert.Empty(await context.Database.ListRepeatedSizeValuesAsync(library.Id, afterLength: 20, limit: 1));
        Assert.Equal(FirstSizePaths, (await context.Database.ListAssetsBySizeAsync(library.Id, 10)).Select(asset => asset.RelativePath));
    }

    [Fact]
    public async Task ConfirmedGroupsRequireSameSizeDigestAndAlgorithmVersion()
    {
        await using var context = await TestContext.CreateAsync();
        var library = await context.Database.AddLibraryAsync("test", "/photos");
        var timestamp = DateTimeOffset.UtcNow;
        foreach (var entry in new[] { ("a", 10, HashA, 1), ("b", 10, HashA, 1), ("c", 11, HashA, 1), ("d", 10, HashA, 2), ("e", 10, HashB, 1) })
        {
            await context.Database.UpsertAssetAsync(library.Id, entry.Item1, entry.Item2, timestamp);
            Assert.True(await context.Database.SaveHashEvidenceAsync(library.Id, entry.Item1, entry.Item2, timestamp, "BLAKE3", entry.Item4, entry.Item3));
        }

        var groups = await context.Database.ListConfirmedDuplicateGroupsAsync(library.Id, "BLAKE3", 1);
        var group = Assert.Single(groups);
        Assert.Equal(ExpectedDuplicatePaths, group.Select(asset => asset.RelativePath));
        Assert.All(group, asset => Assert.Equal(HashA, asset.FullHash));
    }

    [Fact]
    public async Task InitializeMigratesAnExistingCatalogWithoutLosingAssets()
    {
        await using var context = await TestContext.CreateAsync(initialize: false);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = context.Path }.ToString()))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE library_roots (id TEXT PRIMARY KEY NOT NULL, display_name TEXT NOT NULL, canonical_path TEXT NOT NULL UNIQUE, created_at_utc TEXT NOT NULL);
                CREATE TABLE assets (id TEXT PRIMARY KEY NOT NULL, library_id TEXT NOT NULL REFERENCES library_roots(id), relative_path TEXT NOT NULL, length_bytes INTEGER NOT NULL, last_write_utc TEXT NOT NULL, UNIQUE(library_id, relative_path));
                INSERT INTO library_roots VALUES ('11111111-1111-1111-1111-111111111111', 'legacy', '/legacy', '2026-01-01T00:00:00.0000000+00:00');
                INSERT INTO assets VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'old.jpg', 7, '2026-01-01T00:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await context.Database.InitializeAsync();
        var assets = await context.Database.ListAssetsAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var asset = Assert.Single(assets);
        Assert.Equal("old.jpg", asset.RelativePath);
        Assert.Null(asset.FullHash);
    }

    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly string[] ExpectedCandidatePaths = ["b.jpg"];
    private static readonly string[] ExpectedRepeatedCandidatePaths = ["same-a.jpg", "same-b.jpg"];
    private static readonly string[] ExpectedDuplicatePaths = ["a", "b"];

    private sealed class TestContext(string path) : IAsyncDisposable
    {
        public string Path { get; } = path;
        public SqliteLibraryCatalogDataAccessDatabaseService Database { get; } = new(path);

        public static async Task<TestContext> CreateAsync(bool initialize = true)
        {
            var context = new TestContext(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"heimdall-{Guid.NewGuid():N}.db"));
            if (initialize) await context.Database.InitializeAsync();
            return context;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(Path)) File.Delete(Path);
            return ValueTask.CompletedTask;
        }
    }
}
