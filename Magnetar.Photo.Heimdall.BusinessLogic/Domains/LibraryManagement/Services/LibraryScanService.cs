using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Mappers;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;

public sealed class LibraryScanService(ILibraryCatalog catalog, IMediaFileScanner scanner)
{
    public async Task<LibraryRoot> RegisterLibraryAsync(string displayName, string rootPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Library root does not exist: {rootPath}");
        }

        await catalog.InitializeAsync(cancellationToken);
        return await catalog.AddLibraryAsync(displayName, rootPath, cancellationToken);
    }

    public async Task<ScanResult> ScanAsync(LibraryRoot library, CancellationToken cancellationToken = default)
    {
        var count = 0;
        await foreach (var file in scanner.ScanAsync(library.CanonicalPath, cancellationToken))
        {
            await catalog.UpsertAssetAsync(library.Id, file, cancellationToken);
            count++;
        }

        return ScanResultMapper.FromCatalogueCount(library.Id, count);
    }
}
