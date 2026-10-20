using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Mappers;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;

namespace Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;

public sealed class LibraryCatalogDataAccessService(
    ILibraryCatalogDataAccessDatabaseService databaseService,
    IMediaFileScannerDataAccessIoService mediaFileScanner) : ILibraryCatalogDataAccessService
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => databaseService.InitializeAsync(cancellationToken);

    public async Task<LibraryRootDataAccessModel> AddLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default) =>
        LibraryCatalogDataAccessMapper.ToDataAccessModel(await databaseService.AddLibraryAsync(displayName, canonicalPath, cancellationToken));

    public async Task<LibraryRootDataAccessModel> GetOrCreateLibraryAsync(string displayName, string canonicalPath, CancellationToken cancellationToken = default) =>
        LibraryCatalogDataAccessMapper.ToDataAccessModel(await databaseService.GetOrCreateLibraryAsync(displayName, canonicalPath, cancellationToken));

    public async Task<int> ScanLibraryAsync(LibraryRootDataAccessModel library, CancellationToken cancellationToken = default)
    {
        var count = 0;
        var scannedPaths = new List<string>();
        await foreach (var file in mediaFileScanner.ScanAsync(library.CanonicalPath, cancellationToken))
        {
            await databaseService.UpsertAssetAsync(library.Id, file.RelativePath, file.Length, file.LastWriteUtc, cancellationToken);
            scannedPaths.Add(file.RelativePath);
            count++;
        }

        await databaseService.RemoveAssetsMissingFromScanAsync(library.Id, scannedPaths, cancellationToken);
        return count;
    }

    public Task<bool> SaveHashEvidenceAsync(Guid libraryId, string relativePath, long expectedLength, DateTimeOffset expectedLastWriteUtc, string algorithm, int algorithmVersion, string fullHash, CancellationToken cancellationToken = default) =>
        databaseService.SaveHashEvidenceAsync(libraryId, relativePath, expectedLength, expectedLastWriteUtc, algorithm, algorithmVersion, fullHash, cancellationToken);

    public async Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default) =>
        (await databaseService.ListAssetsAsync(libraryId, cancellationToken)).Select(LibraryCatalogDataAccessMapper.ToDataAccessModel).ToArray();

    public Task<int> CountAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default) =>
        databaseService.CountAssetsAsync(libraryId, cancellationToken);

    public async Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListRepeatedSizeAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default) =>
        (await databaseService.ListRepeatedSizeAssetsAsync(libraryId, cancellationToken)).Select(LibraryCatalogDataAccessMapper.ToDataAccessModel).ToArray();

    public Task<IReadOnlyList<long>> ListRepeatedSizeValuesAsync(Guid libraryId, long? afterLength, int limit, CancellationToken cancellationToken = default) =>
        databaseService.ListRepeatedSizeValuesAsync(libraryId, afterLength, limit, cancellationToken);

    public async Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListAssetsBySizeAsync(Guid libraryId, long length, CancellationToken cancellationToken = default) =>
        (await databaseService.ListAssetsBySizeAsync(libraryId, length, cancellationToken)).Select(LibraryCatalogDataAccessMapper.ToDataAccessModel).ToArray();

    public async Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListHashCandidatesBySizeAsync(Guid libraryId, string algorithm, int algorithmVersion, CancellationToken cancellationToken = default) =>
        (await databaseService.ListHashCandidatesBySizeAsync(libraryId, algorithm, algorithmVersion, cancellationToken)).Select(LibraryCatalogDataAccessMapper.ToDataAccessModel).ToArray();

    public async Task<IReadOnlyList<IReadOnlyList<CataloguedAssetDataAccessModel>>> ListConfirmedDuplicateGroupsAsync(Guid libraryId, string algorithm, int algorithmVersion, CancellationToken cancellationToken = default) =>
        (await databaseService.ListConfirmedDuplicateGroupsAsync(libraryId, algorithm, algorithmVersion, cancellationToken))
            .Select(group => (IReadOnlyList<CataloguedAssetDataAccessModel>)group.Select(LibraryCatalogDataAccessMapper.ToDataAccessModel).ToArray()).ToArray();
}
