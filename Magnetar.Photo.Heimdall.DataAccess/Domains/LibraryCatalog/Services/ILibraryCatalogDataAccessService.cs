using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;

/// <summary>Coordinates Database and IO sublayers while hiding their concrete models from callers.</summary>
public interface ILibraryCatalogDataAccessService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<LibraryRootDataAccessModel> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default);
    Task<LibraryRootDataAccessModel> GetOrCreateLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default);
    Task<int> ScanLibraryAsync(LibraryRootDataAccessModel library, CancellationToken cancellationToken = default);
    Task<bool> SaveHashEvidenceAsync(Guid libraryId, string relativePath, long expectedLength, DateTimeOffset expectedLastWriteUtc, string algorithm, int algorithmVersion, string fullHash, CancellationToken cancellationToken = default);
    Task<int> CountAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListRepeatedSizeAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> ListRepeatedSizeValuesAsync(Guid libraryId, long? afterLength, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListAssetsBySizeAsync(Guid libraryId, long length, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListHashCandidatesBySizeAsync(Guid libraryId, string algorithm, int algorithmVersion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IReadOnlyList<CataloguedAssetDataAccessModel>>> ListConfirmedDuplicateGroupsAsync(Guid libraryId, string algorithm, int algorithmVersion, CancellationToken cancellationToken = default);
}
