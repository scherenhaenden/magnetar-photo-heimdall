using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.DataAccessComposition.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Magnetar.Photo.Heimdall.Desktop;

// The composition root is deliberately the only desktop entry point that knows
// about persistence and physical scanning. Views and view models use PresentationLogic only.
internal static class DesktopComposition
{
    public static LibraryOnboardingPresentationLogicService CreateOnboardingFacade()
    {
        var databasePath = GetCatalogDatabasePath();
        var dataAccess = new ServiceCollection()
            .AddHeimdallDataAccess(databasePath)
            .BuildServiceProvider();
        var catalog = dataAccess.GetRequiredService<ILibraryCatalogDataAccessService>();
        return new LibraryOnboardingPresentationLogicService(new LibraryScanBusinessLogicService(catalog));
    }

    internal static string GetCatalogDatabasePath()
    {
        var baseDirectory = OperatingSystem.IsLinux()
            ? Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            : null;
        baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : baseDirectory;
        baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : baseDirectory;

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new InvalidOperationException("A writable per-user application-data directory is required for the Heimdall catalogue.");
        }

        var catalogueDirectory = Path.Combine(baseDirectory, "Magnetar", "PhotoHeimdall");
        Directory.CreateDirectory(catalogueDirectory);
        return Path.Combine(catalogueDirectory, "heimdall-catalog.db");
    }
}
