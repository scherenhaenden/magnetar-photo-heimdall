using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Mappers;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;

public sealed class LibraryScanService(ILibraryCatalogDataAccessService catalog)
{
    public async Task<LibraryRootDataAccessModel> RegisterLibraryAsync(string displayName, string rootPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Library root does not exist: {rootPath}");
        }

        await catalog.InitializeAsync(cancellationToken);
        return await catalog.AddLibraryAsync(displayName, rootPath, cancellationToken);
    }

    public async Task<ScanResult> ScanAsync(LibraryRootDataAccessModel library, CancellationToken cancellationToken = default)
    {
        var count = await catalog.ScanLibraryAsync(library, cancellationToken);
        return ScanResultMapper.FromCatalogueCount(library.Id, count);
    }
}
