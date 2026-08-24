namespace Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;

public sealed record LibraryRoot(Guid Id, string DisplayName, string CanonicalPath, DateTimeOffset CreatedAt);
public sealed record CataloguedAsset(Guid Id, Guid LibraryId, string RelativePath, long Length, DateTimeOffset LastWriteUtc);
