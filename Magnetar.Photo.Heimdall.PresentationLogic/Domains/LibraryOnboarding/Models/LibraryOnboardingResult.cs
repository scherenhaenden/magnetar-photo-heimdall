using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Models;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;

namespace Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Models;

public sealed record LibraryOnboardingResult(LibraryRoot Library, ScanResult Scan);
