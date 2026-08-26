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

    public async Task<int> ScanLibraryAsync(LibraryRootDataAccessModel library, CancellationToken cancellationToken = default)
    {
        var count = 0;
        await foreach (var file in mediaFileScanner.ScanAsync(library.CanonicalPath, cancellationToken))
        {
            await databaseService.UpsertAssetAsync(library.Id, file.RelativePath, file.Length, file.LastWriteUtc, cancellationToken);
            count++;
        }

        return count;
    }

    public async Task<IReadOnlyList<CataloguedAssetDataAccessModel>> ListAssetsAsync(Guid libraryId, CancellationToken cancellationToken = default) =>
        (await databaseService.ListAssetsAsync(libraryId, cancellationToken)).Select(LibraryCatalogDataAccessMapper.ToDataAccessModel).ToArray();
}
