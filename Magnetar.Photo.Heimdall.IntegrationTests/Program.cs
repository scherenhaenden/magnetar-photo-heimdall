using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.DataAccessComposition.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Models;
using Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers.Binary;

var testRoot = Path.Combine(Path.GetTempPath(), $"heimdall-integration-{Guid.NewGuid():N}");
Directory.CreateDirectory(testRoot);

try
{
    var nested = Directory.CreateDirectory(Path.Combine(testRoot, "2026", "trip"));
    await File.WriteAllBytesAsync(Path.Combine(testRoot, "one.jpg"), [1, 2, 3]);
    await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "two.NEF"), [4, 5, 6, 7]);
    await File.WriteAllTextAsync(Path.Combine(nested.FullName, "ignored.txt"), "not media");

    var databasePath = Path.Combine(testRoot, "catalog.db");
    var catalogServices = new ServiceCollection()
        .AddHeimdallDataAccess(databasePath)
        .BuildServiceProvider();
    var catalog = catalogServices.GetRequiredService<ILibraryCatalogDataAccessService>();
    var service = new LibraryScanService(catalog);
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

    var diDatabasePath = Path.Combine(testRoot, "catalog-from-di.db");
    var dataAccessServices = new ServiceCollection()
        .AddHeimdallDataAccess(diDatabasePath)
        .BuildServiceProvider();
    var diCatalog = dataAccessServices.GetRequiredService<ILibraryCatalogDataAccessService>();
    await diCatalog.InitializeAsync();
    var diLibrary = await diCatalog.AddLibraryAsync("DI library", testRoot);
    var diCount = await diCatalog.ScanLibraryAsync(diLibrary);
    Assert(diCount == 2 && (await diCatalog.ListAssetsAsync(diLibrary.Id)).Count == 2,
        "The DataAccess DI boundary must resolve real SQLite and physical-filesystem services.");
    var remoteTransport = dataAccessServices.GetRequiredService<IRemoteAgentTransportDataAccessNetService>();
    var registeredEndpoint = remoteTransport.RegisterEndpoint(new RemoteAgentEndpointDataAccessNetModel(
        "studio-nas", new Uri("https://studio-nas.example.test/agent"), RemoteAgentTransportDataAccessNetKind.SshTunnel));
    Assert(registeredEndpoint.AgentId == "studio-nas" && registeredEndpoint.TransportKind == RemoteAgentTransportDataAccessNetKind.SshTunnel,
        "The DataAccess.Net DI boundary must register and validate a remote agent endpoint without opening a network connection.");
    AssertThrows<ArgumentNullException>(
        () => remoteTransport.RegisterEndpoint(new RemoteAgentEndpointDataAccessNetModel(
            "invalid-agent", null!, RemoteAgentTransportDataAccessNetKind.SshTunnel)),
        "The DataAccess.Net boundary must reject a null endpoint before URI dereferencing.");

    var metadataReader = new MetadataExtractorMediaMetadataReader();
    var metadataFreePath = Path.Combine(testRoot, "no-metadata.bin");
    await File.WriteAllBytesAsync(metadataFreePath, [9, 8, 7]);
    var expectedMtime = new DateTimeOffset(2024, 04, 05, 06, 07, 08, TimeSpan.Zero);
    File.SetLastWriteTimeUtc(metadataFreePath, expectedMtime.UtcDateTime);
    var mtime = await metadataReader.ReadCaptureDateAsync(metadataFreePath);
    Assert(mtime.Source == DateSource.FilesystemMtime, "Files without embedded metadata must explicitly report mtime provenance.");
    Assert(mtime.Value == expectedMtime, "The mtime fallback must retain the filesystem timestamp.");

    var xmpPath = Path.Combine(testRoot, "xmp.jpg");
    await File.WriteAllTextAsync(xmpPath, "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" xmlns:xmp=\"http://ns.adobe.com/xap/\"><xmp:CreateDate>2023-02-03T04:05:06Z</xmp:CreateDate></x:xmpmeta>");
    var xmp = await metadataReader.ReadCaptureDateAsync(xmpPath);
    Assert(xmp.Source == DateSource.Xmp && xmp.Value == new DateTimeOffset(2023, 02, 03, 04, 05, 06, TimeSpan.Zero), "XMP element dates must precede file mtime and retain their provenance.");

    var hashPath = Path.Combine(testRoot, "hash-source.bin");
    var hashBytes = Enumerable.Range(0, 3 * 1024 * 1024).Select(i => (byte)(i % 251)).ToArray();
    await File.WriteAllBytesAsync(hashPath, hashBytes);
    var hasher = new Blake3ContentHasher();
    var firstHash = await hasher.ComputeAsync(hashPath);
    var secondHash = await hasher.ComputeAsync(hashPath);
    Assert(firstHash == secondHash, "Streaming BLAKE3 hashes must be deterministic for the same real file.");
    hashBytes[^1] ^= 0xff;
    await File.WriteAllBytesAsync(hashPath, hashBytes);
    var changedHash = await hasher.ComputeAsync(hashPath);
    Assert(changedHash.FullHash != firstHash.FullHash && changedHash.QuickFingerprint != firstHash.QuickFingerprint, "Changing bytes in a real file must change both full and quick BLAKE3 hashes.");
    Assert(firstHash.QuickFingerprintVersion == Blake3ContentHasher.FingerprintVersion, "Quick fingerprints must carry their explicit format version.");

    var v1QuickTimePath = Path.Combine(testRoot, "late-v1.mov");
    await using (var output = new FileStream(v1QuickTimePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
    {
        await output.WriteAsync(new byte[(16 * 1024 * 1024) + 9]);
        await output.WriteAsync(CreateMvhdV1(new DateTimeOffset(2022, 08, 09, 10, 11, 12, TimeSpan.Zero)));
    }
    var quickTime = await metadataReader.ReadCaptureDateAsync(v1QuickTimePath);
    Assert(quickTime.Source == DateSource.QuickTime && quickTime.Value == new DateTimeOffset(2022, 08, 09, 10, 11, 12, TimeSpan.Zero), "QuickTime v1 mvhd after 16 MiB must resolve its creation date.");

    var malformedExifPath = Path.Combine(testRoot, "malformed-exif.jpg");
    await File.WriteAllBytesAsync(malformedExifPath, [.. "Exif\0\0II*\0\x08\0\0\0\xff\xff"u8]);
    var malformed = await metadataReader.ReadCaptureDateAsync(malformedExifPath);
    Assert(malformed.Source == DateSource.FilesystemMtime, "Malformed EXIF offsets must fall back without throwing.");

    var services = new ServiceCollection().AddMediaAnalysis().BuildServiceProvider();
    Assert(services.GetRequiredService<IMediaMetadataReader>() is MetadataExtractorMediaMetadataReader && services.GetRequiredService<IContentHasher>() is Blake3ContentHasher, "AddMediaAnalysis must register the concrete metadata and content hashing services.");
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

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static byte[] CreateMvhdV1(DateTimeOffset capturedAt)
{
    var bytes = new byte[32];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
    "mvhd"u8.CopyTo(bytes.AsSpan(4));
    bytes[8] = 1;
    var seconds = checked((ulong)(capturedAt - DateTimeOffset.UnixEpoch).TotalSeconds + 2_082_844_800UL);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(12), seconds);
    return bytes;
}
