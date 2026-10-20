using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;

public interface ILibraryCatalogDataAccessDatabaseService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<LibraryRootDataAccessDatabaseModel> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default);
    Task<LibraryRootDataAccessDatabaseModel> GetOrCreateLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default);
    Task UpsertAssetAsync(Guid libraryId, string relativePath, long length, DateTimeOffset lastWriteUtc, CancellationToken cancellationToken = default);
    Task RemoveAssetsMissingFromScanAsync(Guid libraryId, IReadOnlyCollection<string> scannedRelativePaths, CancellationToken cancellationToken = default);
    Task<bool> SaveHashEvidenceAsync(Guid libraryId, string relativePath, long expectedLength, DateTimeOffset expectedLastWriteUtc, string algorithm, int algorithmVersion, string fullHash, CancellationToken cancellationToken = default);
    Task<int> CountAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListRepeatedSizeAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> ListRepeatedSizeValuesAsync(Guid libraryId, long? afterLength, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListAssetsBySizeAsync(Guid libraryId, long length, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>> ListHashCandidatesBySizeAsync(Guid libraryId, string algorithm, int algorithmVersion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IReadOnlyList<CataloguedAssetDataAccessDatabaseModel>>> ListConfirmedDuplicateGroupsAsync(Guid libraryId, string algorithm, int algorithmVersion, CancellationToken cancellationToken = default);
}
