namespace Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Models;

/// <summary>Database-owned representation of a catalogued media asset.</summary>
public sealed record CataloguedAssetDataAccessDatabaseModel(Guid Id, Guid LibraryId, string RelativePath, long Length, DateTimeOffset LastWriteUtc);