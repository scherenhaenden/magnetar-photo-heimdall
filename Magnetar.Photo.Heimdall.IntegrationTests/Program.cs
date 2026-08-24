using Magnetar.Photo.Heimdall.BusinessLogic;
using Magnetar.Photo.Heimdall.DataAccess;
using Magnetar.Photo.Heimdall.DataAccess.IO;
using Magnetar.Photo.Heimdall.MediaAnalysis;

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

    var metadataReader = new MediaMetadataReader();
    var metadataFreePath = Path.Combine(testRoot, "no-metadata.bin");
    await File.WriteAllBytesAsync(metadataFreePath, [9, 8, 7]);
    var expectedMtime = new DateTimeOffset(2024, 04, 05, 06, 07, 08, TimeSpan.Zero);
    File.SetLastWriteTimeUtc(metadataFreePath, expectedMtime.UtcDateTime);
    var mtime = await metadataReader.ReadCapturedAtAsync(metadataFreePath);
    Assert(mtime.Source == CapturedAtSource.FileModifiedTime, "Files without embedded metadata must explicitly report mtime provenance.");
    Assert(mtime.Value == expectedMtime, "The mtime fallback must retain the filesystem timestamp.");

    var xmpPath = Path.Combine(testRoot, "xmp.jpg");
    await File.WriteAllTextAsync(xmpPath, "<x:xmpmeta xmp:CreateDate=\"2023-02-03T04:05:06Z\" />");
    var xmp = await metadataReader.ReadCapturedAtAsync(xmpPath);
    Assert(xmp.Source == CapturedAtSource.Xmp && xmp.Value == new DateTimeOffset(2023, 02, 03, 04, 05, 06, TimeSpan.Zero), "XMP dates must precede file mtime and retain their provenance.");

    var hashPath = Path.Combine(testRoot, "hash-source.bin");
    var hashBytes = Enumerable.Range(0, 3 * 1024 * 1024).Select(i => (byte)(i % 251)).ToArray();
    await File.WriteAllBytesAsync(hashPath, hashBytes);
    var hasher = new Blake3MediaHasher();
    var firstHash = await hasher.HashAsync(hashPath);
    var secondHash = await hasher.HashAsync(hashPath);
    Assert(firstHash == secondHash, "Streaming BLAKE3 hashes must be deterministic for the same real file.");
    hashBytes[^1] ^= 0xff;
    await File.WriteAllBytesAsync(hashPath, hashBytes);
    var changedHash = await hasher.HashAsync(hashPath);
    Assert(changedHash.Full != firstHash.Full && changedHash.Quick != firstHash.Quick, "Changing bytes in a real file must change both full and quick BLAKE3 hashes.");
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
