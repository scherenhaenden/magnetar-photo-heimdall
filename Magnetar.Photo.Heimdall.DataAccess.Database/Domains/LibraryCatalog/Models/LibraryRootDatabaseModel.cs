namespace Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Models;

/// <summary>Database-owned representation of a registered library root.</summary>
public sealed record LibraryRootDatabaseModel(Guid Id, string DisplayName, string CanonicalPath, DateTimeOffset CreatedAt);