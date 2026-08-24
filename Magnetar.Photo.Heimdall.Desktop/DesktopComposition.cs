using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;

namespace Magnetar.Photo.Heimdall.Desktop;

// The composition root is deliberately the only desktop entry point that knows
// about persistence and physical scanning. Views and view models use PresentationLogic only.
internal static class DesktopComposition
{
    public static LibraryOnboardingFacade CreateOnboardingFacade()
    {
        var databasePath = Path.Combine(AppContext.BaseDirectory, "heimdall-catalog.db");
        var databaseService = new SqliteLibraryCatalogDatabaseService(databasePath);
        var scanner = new PhysicalMediaFileScannerDataAccessIoService();
        var catalog = new LibraryCatalogDataAccessService(databaseService, scanner);
        return new LibraryOnboardingFacade(new LibraryScanService(catalog));
    }
}
