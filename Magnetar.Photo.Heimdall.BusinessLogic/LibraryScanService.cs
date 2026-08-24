using Magnetar.Photo.Heimdall.DataAccess;
using Magnetar.Photo.Heimdall.DataAccess.IO;

namespace Magnetar.Photo.Heimdall.BusinessLogic;

public sealed record ScanResult(Guid LibraryId, int CataloguedAssetCount);

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

        return new ScanResult(library.Id, count);
    }
}
