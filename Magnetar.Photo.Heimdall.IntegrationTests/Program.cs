using Magnetar.Photo.Heimdall.BusinessLogic;
using Magnetar.Photo.Heimdall.DataAccess;
using Magnetar.Photo.Heimdall.DataAccess.IO;

var testRoot = Path.Combine(Path.GetTempPath(), $"heimdall-integration-{Guid.NewGuid():N}");
Directory.CreateDirectory(testRoot);

try
{
    var nested = Directory.CreateDirectory(Path.Combine(testRoot, "2026", "trip"));
    await File.WriteAllBytesAsync(Path.Combine(testRoot, "one.jpg"), [1, 2, 3]);
    await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "two.NEF"), [4, 5, 6, 7]);
    await File.WriteAllTextAsync(Path.Combine(nested.FullName, "ignored.txt"), "not media");

    var databasePath = Path.Combine(testRoot, "catalog.db");
    var catalog = new SqliteLibraryCatalog(databasePath);
    var service = new LibraryScanService(catalog, new PhysicalMediaFileScanner());
    var library = await service.RegisterLibraryAsync("Integration library", testRoot);
    var firstScan = await service.ScanAsync(library);
    var firstAssets = await catalog.ListAssetsAsync(library.Id);

    Assert(firstScan.CataloguedAssetCount == 2, "The real recursive scan must find exactly two supported media files.");
    Assert(firstAssets.Count == 2, "The real SQLite catalog must persist two assets.");
    Assert(firstAssets.Select(asset => asset.RelativePath).SequenceEqual(["2026/trip/two.NEF", "one.jpg"]), "Catalog paths must be relative and deterministically ordered.");

    await File.WriteAllBytesAsync(Path.Combine(testRoot, "one.jpg"), [1, 2, 3, 4, 5]);
    var secondScan = await service.ScanAsync(library);
    var secondAssets = await catalog.ListAssetsAsync(library.Id);

    Assert(secondScan.CataloguedAssetCount == 2, "A second scan must re-observe the real files.");
    Assert(secondAssets.Count == 2, "Upsert must not duplicate an existing asset location.");
    Assert(secondAssets.Single(asset => asset.RelativePath == "one.jpg").Length == 5, "A changed real file must update its SQLite catalog record.");
    Console.WriteLine("PASS: real filesystem + SQLite integration scan.");
    return 0;
}
finally
{
    Directory.Delete(testRoot, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
