using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Models;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Mappers;

namespace Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;

public sealed class LibraryOnboardingPresentationLogicService(LibraryScanBusinessLogicService libraryScanService)
{
    public async Task<LibraryOnboardingResultPresentationLogicModel> AddAndScanAsync(string displayName, string rootPath, CancellationToken cancellationToken = default)
    {
        var library = await libraryScanService.RegisterLibraryAsync(displayName, rootPath, cancellationToken);
        var scan = await libraryScanService.ScanAsync(library, cancellationToken);
        return LibraryOnboardingResultPresentationLogicMapper.From(library, scan);
    }
}
