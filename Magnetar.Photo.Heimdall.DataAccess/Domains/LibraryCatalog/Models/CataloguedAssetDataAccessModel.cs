namespace Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;

/// <summary>DataAccess-layer representation of an asset, independent of any persistence provider.</summary>
public sealed record CataloguedAssetDataAccessModel(Guid Id, Guid LibraryId, string RelativePath, long Length, DateTimeOffset LastWriteUtc);