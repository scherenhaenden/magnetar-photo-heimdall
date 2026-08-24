using Magnetar.Photo.Heimdall.BusinessLogic;
using Magnetar.Photo.Heimdall.DataAccess;
using Magnetar.Photo.Heimdall.DataAccess.IO;
using Magnetar.Photo.Heimdall.PresentationLogic;

namespace Magnetar.Photo.Heimdall.Desktop;

// The composition root is deliberately the only desktop entry point that knows
// about persistence and physical scanning. Views and view models use PresentationLogic only.
internal static class DesktopComposition
{
    public static LibraryOnboardingFacade CreateOnboardingFacade()
    {
        var databasePath = Path.Combine(AppContext.BaseDirectory, "heimdall-catalog.db");
        var catalog = new SqliteLibraryCatalog(databasePath);
        var scanner = new PhysicalMediaFileScanner();
        return new LibraryOnboardingFacade(new LibraryScanService(catalog, scanner));
    }
}
