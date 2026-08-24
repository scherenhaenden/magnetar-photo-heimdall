using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Models;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Mappers;

namespace Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;

public sealed class LibraryOnboardingFacade(LibraryScanService libraryScanService)
{
    public async Task<LibraryOnboardingResult> AddAndScanAsync(string displayName, string rootPath, CancellationToken cancellationToken = default)
    {
        var library = await libraryScanService.RegisterLibraryAsync(displayName, rootPath, cancellationToken);
        var scan = await libraryScanService.ScanAsync(library, cancellationToken);
        return LibraryOnboardingResultMapper.From(library, scan);
    }
}
