using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Magnetar.Photo.Heimdall.DataAccess.Database.Tests;

public sealed class RepeatedSizeIndexTests
{
    [Fact]
    public async Task RepeatedSizeGroupingUsesCompositeCatalogIndex()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"heimdall-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SqliteLibraryCatalogDataAccessDatabaseService(databasePath);
            await database.InitializeAsync();
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN SELECT length_bytes FROM assets WHERE library_id = $libraryId GROUP BY length_bytes HAVING COUNT(*) > 1;";
            command.Parameters.AddWithValue("$libraryId", Guid.NewGuid().ToString("D"));
            await using var reader = await command.ExecuteReaderAsync();
            var details = new List<string>();
            while (await reader.ReadAsync()) details.Add(reader.GetString(3));

            Assert.Contains(details, detail => detail.Contains("ix_assets_library_length_path", StringComparison.Ordinal));
            Assert.DoesNotContain(details, detail => detail.Contains("TEMP B-TREE", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }
}
