using Magnetar.Photo.Heimdall.BusinessLogic;
using Magnetar.Photo.Heimdall.DataAccess;

namespace Magnetar.Photo.Heimdall.PresentationLogic;

public sealed record LibraryOnboardingResult(LibraryRoot Library, ScanResult Scan);

public sealed class LibraryOnboardingFacade(LibraryScanService libraryScanService)
{
    public async Task<LibraryOnboardingResult> AddAndScanAsync(string displayName, string rootPath, CancellationToken cancellationToken = default)
    {
        var library = await libraryScanService.RegisterLibraryAsync(displayName, rootPath, cancellationToken);
        var scan = await libraryScanService.ScanAsync(library, cancellationToken);
        return new LibraryOnboardingResult(library, scan);
    }
}
