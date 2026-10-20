using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Mappers;
using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Models;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;

public sealed class SqliteLibraryCatalogDataAccessDatabaseService(string databasePath) : ILibraryCatalogDataAccessDatabaseService
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS library_roots (
                id TEXT PRIMARY KEY NOT NULL,
                display_name TEXT NOT NULL,
                canonical_path TEXT NOT NULL UNIQUE,
                created_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS assets (
                id TEXT PRIMARY KEY NOT NULL,
                library_id TEXT NOT NULL REFERENCES library_roots(id),
                relative_path TEXT NOT NULL,
                length_bytes INTEGER NOT NULL,
                last_write_utc TEXT NOT NULL,
                hash_algorithm TEXT NULL,
                hash_algorithm_version INTEGER NULL,
                full_hash TEXT NULL,
                UNIQUE(library_id, relative_path)
            );
            -- Covers repeated-size grouping and the ordered candidate walk with one index.
            CREATE INDEX IF NOT EXISTS ix_assets_library_length_path ON assets(library_id, length_bytes, relative_path);
            DROP INDEX IF EXISTS ix_assets_library_length;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureHashColumnsAsync(connection, cancellationToken);
    }

    public async Task<LibraryRootDataAccessDatabaseModel> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        var library = new LibraryRootDataAccessDatabaseModel(Guid.NewGuid(), displayName, Path.GetFullPath(canonicalPath), DateTimeOffset.UtcNow);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO library_roots (id, display_name, canonical_path, created_at_utc) VALUES ($id, $name, $path, $createdAt);";
        command.Parameters.AddWithValue("$id", library.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", library.DisplayName);
        command.Parameters.AddWithValue("$path", library.CanonicalPath);
        command.Parameters.AddWithValue("$createdAt", library.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return library;
    }

    public async Task<LibraryRootDataAccessDatabaseModel> GetOrCreateLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        var fullPath = Path.GetFullPath(canonicalPath);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO library_roots (id, display_name, canonical_path, created_at_utc) VALUES ($id, $name, $path, $createdAt) ON CONFLICT(canonical_path) DO NOTHING";
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$name", displayName);
            insert.Parameters.AddWithValue("$path", fullPath);
            insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT id, display_name, canonical_path, created_at_utc FROM library_roots WHERE canonical_path = $path";
        select.Parameters.AddWithValue("$path", fullPath);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Library root registration did not persist.");
        return new LibraryRootDataAccessDatabaseModel(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public async Task UpsertAssetAsync(Guid libraryId, string relativePath, long length, DateTimeOffset lastWriteUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO assets (id, library_id, relative_path, length_bytes, last_write_utc)
            VALUES ($id, $libraryId, $path, $length, $lastWrite)
            ON CONFLICT(library_id, relative_path) DO UPDATE SET
                hash_algorithm = CASE WHEN assets.length_bytes = excluded.length_bytes AND assets.last_write_utc = excluded.last_write_utc THEN assets.hash_algorithm ELSE NULL END,
                hash_algorithm_version = CASE WHEN assets.length_bytes = excluded.length_bytes AND assets.last_write_utc = excluded.last_write_utc THEN assets.hash_algorithm_version ELSE NULL END,
                full_hash = CASE WHEN assets.length_bytes = excluded.length_bytes AND assets.last_write_utc = excluded.last_write_utc THEN assets.full_hash ELSE NULL END,
                length_bytes = excluded.length_bytes, last_write_utc = excluded.last_write_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        command.Parameters.AddWithValue("$path", relativePath);
        command.Parameters.AddWithValue("$length", length);
        command.Parameters.AddWithValue("$lastWrite", lastWriteUtc.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAssetsMissingFromScanAsync(Guid libraryId, IReadOnlyCollection<string> scannedRelativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scannedRelativePaths);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var create = connection.CreateCommand())
        {
            create.Transaction = (SqliteTransaction)transaction;
            create.CommandText = "DROP TABLE IF EXISTS temp.scanned_paths; CREATE TEMP TABLE scanned_paths (relative_path TEXT PRIMARY KEY NOT NULL)";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO scanned_paths(relative_path) VALUES ($path)";
            var pathParameter = insert.Parameters.Add("$path", SqliteType.Text);
            foreach (var path in scannedRelativePaths.Distinct(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                pathParameter.Value = path;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM assets WHERE library_id = $libraryId AND relative_path NOT IN (SELECT relative_path FROM scanned_paths)";
            delete.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> SaveHashEvidenceAsync(Guid libraryId, string relativePath, long expectedLength, DateTimeOffset expectedLastWriteUtc, string algorithm, int algorithmVersion, string fullHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullHash);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(algorithmVersion);
        if (fullHash.Length != 64 || !fullHash.All(Uri.IsHexDigit)) throw new ArgumentException("A full BLAKE3 digest must be 64 hexadecimal characters.", nameof(fullHash));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE assets
            SET hash_algorithm = $algorithm, hash_algorithm_version = $version, full_hash = $hash
            WHERE library_id = $libraryId AND relative_path = $path
              AND length_bytes = $length AND last_write_utc = $lastWrite;
            """;
        command.Parameters.AddWithValue("$algorithm", algorithm);
        command.Parameters.AddWithValue("$version", algorithmVersion);
        command.Parameters.AddWithValue("$hash", fullHash.ToLowerInvariant());
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        command.Parameters.AddWithValue("$path", relativePath);
        command.Parameters.AddWithValue("$length", expectedLength);
        command.Parameters.AddWithValue("$lastWrite", expectedLastWriteUtc.ToUniversalTime().ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default)
    {
        var assets = new List<CataloguedAssetDataAccessDatabaseModel>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, relative_path, length_bytes, last_write_utc, hash_algorithm, hash_algorithm_version, full_hash FROM assets WHERE library_id = $libraryId ORDER BY relative_path;";
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            assets.Add(CataloguedAssetDataAccessDatabaseMapper.FromReader(reader, libraryId));
        }

        return assets;
    }

    public async Task<int> CountAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM assets WHERE library_id = $libraryId;";
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListRepeatedSizeAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default)
    {
        var assets = new List<CataloguedAssetDataAccessDatabaseModel>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.relative_path, a.length_bytes, a.last_write_utc, a.hash_algorithm, a.hash_algorithm_version, a.full_hash
            FROM assets a
            INNER JOIN (
                SELECT length_bytes FROM assets WHERE library_id = $libraryId
                GROUP BY length_bytes HAVING COUNT(*) > 1
            ) repeated ON repeated.length_bytes = a.length_bytes
            WHERE a.library_id = $libraryId
            ORDER BY a.length_bytes, a.relative_path;
            """;
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            assets.Add(CataloguedAssetDataAccessDatabaseMapper.FromReader(reader, libraryId));
        return assets;
    }

    public async Task<IReadOnlyList<long>> ListRepeatedSizeValuesAsync(Guid libraryId, long? afterLength, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var values = new List<long>(limit);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT length_bytes FROM assets WHERE library_id = $libraryId
            GROUP BY length_bytes HAVING COUNT(*) > 1 AND ($afterLength IS NULL OR length_bytes > $afterLength)
            ORDER BY length_bytes LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        command.Parameters.AddWithValue("$afterLength", (object?)afterLength ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) values.Add(reader.GetInt64(0));
        return values;
    }

    public async Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListAssetsBySizeAsync(Guid libraryId, long length, CancellationToken cancellationToken = default)
    {
        var assets = new List<CataloguedAssetDataAccessDatabaseModel>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, relative_path, length_bytes, last_write_utc, hash_algorithm, hash_algorithm_version, full_hash FROM assets WHERE library_id = $libraryId AND length_bytes = $length ORDER BY relative_path;";
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        command.Parameters.AddWithValue("$length", length);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) assets.Add(CataloguedAssetDataAccessDatabaseMapper.FromReader(reader, libraryId));
        return assets;
    }

    public async Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListHashCandidatesBySizeAsync(Guid libraryId, string algorithm, int algorithmVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(algorithmVersion);
        var assets = new List<CataloguedAssetDataAccessDatabaseModel>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.relative_path, a.length_bytes, a.last_write_utc, a.hash_algorithm, a.hash_algorithm_version, a.full_hash
            FROM assets a
            INNER JOIN (SELECT length_bytes FROM assets WHERE library_id = $libraryId GROUP BY length_bytes HAVING COUNT(*) > 1) b
                ON b.length_bytes = a.length_bytes
            WHERE a.library_id = $libraryId AND (a.full_hash IS NULL OR a.hash_algorithm IS NULL OR a.hash_algorithm <> $algorithm
                OR a.hash_algorithm_version IS NULL OR a.hash_algorithm_version <> $version)
            ORDER BY a.length_bytes, a.relative_path;
            """;
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        command.Parameters.AddWithValue("$algorithm", algorithm);
        command.Parameters.AddWithValue("$version", algorithmVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) assets.Add(CataloguedAssetDataAccessDatabaseMapper.FromReader(reader, libraryId));
        return assets;
    }

    public async Task<IReadOnlyList<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>>> ListConfirmedDuplicateGroupsAsync(Guid libraryId, string algorithm, int algorithmVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(algorithmVersion);
        var groups = new List<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.relative_path, a.length_bytes, a.last_write_utc, a.hash_algorithm, a.hash_algorithm_version, a.full_hash
            FROM assets a
            INNER JOIN (
                SELECT length_bytes, full_hash FROM assets
                WHERE library_id = $libraryId AND hash_algorithm = $algorithm AND hash_algorithm_version = $version AND full_hash IS NOT NULL
                GROUP BY length_bytes, full_hash HAVING COUNT(*) > 1
            ) g ON g.length_bytes = a.length_bytes AND g.full_hash = a.full_hash
            WHERE a.library_id = $libraryId AND a.hash_algorithm = $algorithm AND a.hash_algorithm_version = $version
            ORDER BY a.length_bytes, a.full_hash, a.relative_path;
            """;
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        command.Parameters.AddWithValue("$algorithm", algorithm);
        command.Parameters.AddWithValue("$version", algorithmVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        string? currentKey = null;
        List<CataloguedAssetDataAccessDatabaseModel>? currentGroup = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = $"{reader.GetInt64(2)}:{reader.GetString(6)}";
            if (!StringComparer.Ordinal.Equals(key, currentKey))
            {
                currentGroup = [];
                groups.Add(currentGroup);
                currentKey = key;
            }
            currentGroup!.Add(CataloguedAssetDataAccessDatabaseMapper.FromReader(reader, libraryId));
        }
        return groups;
    }

    private static async Task EnsureHashColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(assets);";
        await using (var reader = await check.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) existing.Add(reader.GetString(1));

        foreach (var (name, type) in new[] { ("hash_algorithm", "TEXT NULL"), ("hash_algorithm_version", "INTEGER NULL"), ("full_hash", "TEXT NULL") })
        {
            if (existing.Contains(name)) continue;
            var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE assets ADD COLUMN {name} {type};";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        var index = connection.CreateCommand();
        index.CommandText = "CREATE INDEX IF NOT EXISTS ix_assets_library_hash ON assets(library_id, hash_algorithm, hash_algorithm_version, length_bytes, full_hash);";
        await index.ExecuteNonQueryAsync(cancellationToken);
    }
}
