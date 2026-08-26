using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Models;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Mappers;

internal static class CataloguedAssetDataAccessDatabaseMapper
{
    public static CataloguedAssetDataAccessDatabaseModel FromReader(SqliteDataReader reader, Guid libraryId) =>
        new(Guid.Parse(reader.GetString(0)), libraryId, reader.GetString(1), reader.GetInt64(2),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
