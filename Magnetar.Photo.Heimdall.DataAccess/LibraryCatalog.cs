using Magnetar.Photo.Heimdall.DataAccess.IO;
using Microsoft.Data.Sqlite;

namespace Magnetar.Photo.Heimdall.DataAccess;

public sealed record LibraryRoot(Guid Id, string DisplayName, string CanonicalPath, DateTimeOffset CreatedAt);
public sealed record CataloguedAsset(Guid Id, Guid LibraryId, string RelativePath, long Length, DateTimeOffset LastWriteUtc);

public interface ILibraryCatalog
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<LibraryRoot> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default);
    Task UpsertAssetAsync(Guid libraryId, DiscoveredMediaFile file, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAsset>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
}

public sealed class SqliteLibraryCatalog(string databasePath) : ILibraryCatalog
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
                UNIQUE(library_id, relative_path)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LibraryRoot> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        var library = new LibraryRoot(Guid.NewGuid(), displayName, Path.GetFullPath(canonicalPath), DateTimeOffset.UtcNow);
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

    public async Task UpsertAssetAsync(Guid libraryId, DiscoveredMediaFile file, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO assets (id, library_id, relative_path, length_bytes, last_write_utc)
            VALUES ($id, $libraryId, $path, $length, $lastWrite)
            ON CONFLICT(library_id, relative_path) DO UPDATE SET length_bytes = excluded.length_bytes, last_write_utc = excluded.last_write_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        command.Parameters.AddWithValue("$path", file.RelativePath);
        command.Parameters.AddWithValue("$length", file.Length);
        command.Parameters.AddWithValue("$lastWrite", file.LastWriteUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CataloguedAsset>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default)
    {
        var assets = new List<CataloguedAsset>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, relative_path, length_bytes, last_write_utc FROM assets WHERE library_id = $libraryId ORDER BY relative_path;";
        command.Parameters.AddWithValue("$libraryId", libraryId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            assets.Add(new CataloguedAsset(Guid.Parse(reader.GetString(0)), libraryId, reader.GetString(1), reader.GetInt64(2), DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }

        return assets;
    }
}
