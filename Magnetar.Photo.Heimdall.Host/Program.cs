using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.DataAccessComposition.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;
using Microsoft.Extensions.DependencyInjection;

if (args.Length is not 2)
{
    Console.Error.WriteLine("Usage: heimdall <library-name> <library-path>");
    return 2;
}

var databasePath = Path.Combine(AppContext.BaseDirectory, "heimdall-catalog.db");
var dataAccess = new ServiceCollection()
    .AddHeimdallDataAccess(databasePath)
    .BuildServiceProvider();
var catalog = dataAccess.GetRequiredService<ILibraryCatalogDataAccessService>();
var scanService = new LibraryScanBusinessLogicService(catalog);
var onboarding = new LibraryOnboardingPresentationLogicService(scanService);
var result = await onboarding.AddAndScanAsync(args[0], args[1]);

Console.WriteLine($"Catalogued {result.Scan.CataloguedAssetCount} media assets from '{result.Library.DisplayName}'.");
return 0;
