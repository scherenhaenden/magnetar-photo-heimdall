using Magnetar.Photo.Heimdall.BusinessLogic;
using Magnetar.Photo.Heimdall.DataAccess;
using Magnetar.Photo.Heimdall.DataAccess.IO;
using Magnetar.Photo.Heimdall.PresentationLogic;

if (args.Length is not 2)
{
    Console.Error.WriteLine("Usage: heimdall <library-name> <library-path>");
    return 2;
}

var databasePath = Path.Combine(AppContext.BaseDirectory, "heimdall-catalog.db");
var catalog = new SqliteLibraryCatalog(databasePath);
var scanService = new LibraryScanService(catalog, new PhysicalMediaFileScanner());
var onboarding = new LibraryOnboardingFacade(scanService);
var result = await onboarding.AddAndScanAsync(args[0], args[1]);

Console.WriteLine($"Catalogued {result.Scan.CataloguedAssetCount} media assets from '{result.Library.DisplayName}'.");
return 0;
