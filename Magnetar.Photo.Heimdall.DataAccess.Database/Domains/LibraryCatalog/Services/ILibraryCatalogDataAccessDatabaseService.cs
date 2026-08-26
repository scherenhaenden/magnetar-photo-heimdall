using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;

public interface ILibraryCatalogDataAccessDatabaseService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<LibraryRootDataAccessDatabaseModel> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default);
    Task UpsertAssetAsync(Guid libraryId, string relativePath, long length, DateTimeOffset lastWriteUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
}