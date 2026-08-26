namespace Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;

/// <summary>DataAccess-layer representation of a library, independent of any persistence provider.</summary>
public sealed record LibraryRootDataAccessModel(Guid Id, string DisplayName, string CanonicalPath, DateTimeOffset CreatedAt);