using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Models;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Models;

namespace Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Mappers;

internal static class LibraryOnboardingResultMapper
{
    public static LibraryOnboardingResult From(LibraryRoot library, ScanResult scan) => new(library, scan);
}
