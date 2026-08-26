using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;

/// <summary>Coordinates Database and IO sublayers while hiding their concrete models from callers.</summary>
public interface ILibraryCatalogDataAccessService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<LibraryRootDataAccessModel> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default);
    Task<int> ScanLibraryAsync(LibraryRootDataAccessModel library, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
}