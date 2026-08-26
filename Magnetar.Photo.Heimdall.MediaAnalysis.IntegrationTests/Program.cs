using System.Text;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("=== Starting MediaAnalysis Integration Tests ===");

var tempDir = Path.Combine(Path.GetTempPath(), $"heimdall-media-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);

var reader = new MetadataExtractorMediaMetadataReaderBusinessLogicService();
var hasher = new Blake3ContentHasherBusinessLogicService();

try
{
    // Test 1: JPEG with real EXIF DateTimeOriginal
    Console.WriteLine("Running Test 1: JPEG with real EXIF metadata...");
    var jpegPath = Path.Combine(tempDir, "sample_exif.jpg");
    CreateJpegWithExif(jpegPath, "2026:08:24 14:30:00");

    var dateResult = await reader.ReadCaptureDateAsync(jpegPath);
    Assert(dateResult.Source == DateSourceBusinessLogicModel.ExifDateTimeOriginal, $"Expected ExifDateTimeOriginal but got {dateResult.Source}");
    Assert(Math.Abs(dateResult.Confidence - 1.0) < 0.001, $"Expected confidence 1.0 but got {dateResult.Confidence}");
    Assert(dateResult.Value.Year == 2026 && dateResult.Value.Month == 8 && dateResult.Value.Day == 24, "Date parsed mismatch");
    Assert(dateResult.Value.Hour == 14 && dateResult.Value.Minute == 30 && dateResult.Value.Second == 0, "Time parsed mismatch");
    Assert(dateResult.AllEvidence.Count >= 1, "AllEvidence should have entries");
    Assert(dateResult.AllEvidence[0].Source == DateSourceBusinessLogicModel.ExifDateTimeOriginal, "First evidence should be EXIF");
    Console.WriteLine("  ✓ PASS: JPEG real EXIF DateTimeOriginal extracted successfully.");

    // Test 2: File without EXIF metadata (fallback to filesystem mtime)
    Console.WriteLine("Running Test 2: Fallback to filesystem mtime...");
    var plainPath = Path.Combine(tempDir, "no_exif.bin");
    await File.WriteAllBytesAsync(plainPath, [10, 20, 30, 40, 50]);
    var expectedMtime = new DateTimeOffset(File.GetLastWriteTimeUtc(plainPath), TimeSpan.Zero);

    var fallbackResult = await reader.ReadCaptureDateAsync(plainPath);
    Assert(fallbackResult.Source == DateSourceBusinessLogicModel.FilesystemMtime, $"Expected FilesystemMtime but got {fallbackResult.Source}");
    Assert(Math.Abs(fallbackResult.Confidence - 0.10) < 0.001, $"Expected confidence 0.10 but got {fallbackResult.Confidence}");
    Assert(Math.Abs((fallbackResult.Value - expectedMtime).TotalSeconds) < 2, "Fallback date should match mtime");
    Console.WriteLine("  ✓ PASS: Fallback to filesystem mtime verified.");

    // Test 3: BLAKE3 changes when file bytes are modified
    Console.WriteLine("Running Test 3: BLAKE3 changes when bytes modified...");
    var mutateFile = Path.Combine(tempDir, "mutate.dat");
    await File.WriteAllBytesAsync(mutateFile, Encoding.UTF8.GetBytes("Original Content 1234567890"));
    var initialHash = await hasher.ComputeAsync(mutateFile);

    await File.WriteAllBytesAsync(mutateFile, Encoding.UTF8.GetBytes("Modified Content 1234567890"));
    var modifiedHash = await hasher.ComputeAsync(mutateFile);

    Assert(initialHash.FullHash != modifiedHash.FullHash, "Full hash must change when content changes");
    Assert(initialHash.QuickFingerprint != modifiedHash.QuickFingerprint, "Quick fingerprint must change when content changes");
    Console.WriteLine("  ✓ PASS: BLAKE3 hashes change on content mutation.");

    // Test 4: Determinism of FullHash and QuickFingerprint
    Console.WriteLine("Running Test 4: BLAKE3 hash determinism...");
    var pass1 = await hasher.ComputeAsync(mutateFile);
    var pass2 = await hasher.ComputeAsync(mutateFile);
    Assert(pass1.FullHash == pass2.FullHash, "FullHash must be deterministic");
    Assert(pass1.QuickFingerprint == pass2.QuickFingerprint, "QuickFingerprint must be deterministic");
    Assert(pass1.FileLength == pass2.FileLength, "FileLength must be consistent");
    Assert(pass1.QuickFingerprintVersion == 1, "Quick fingerprint version must be 1");
    Assert(pass1.Algorithm == "BLAKE3", "Algorithm must be BLAKE3");
    Console.WriteLine("  ✓ PASS: BLAKE3 hashes are deterministic.");

    // Test 5: Large file (>2MB) streaming and quick fingerprint behavior
    Console.WriteLine("Running Test 5: Large file streaming (>2MB)...");
    var largePath = Path.Combine(tempDir, "large.bin");
    const int largeFileSize = 3 * 1024 * 1024; // 3 MiB
    await using (var fs = new FileStream(largePath, FileMode.Create, FileAccess.Write, FileShare.None))
    {
        var chunk = new byte[64 * 1024];
        for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)(i % 251);
        int written = 0;
        while (written < largeFileSize)
        {
            await fs.WriteAsync(chunk.AsMemory(0, Math.Min(chunk.Length, largeFileSize - written)));
            written += chunk.Length;
        }
    }

    var largeHash = await hasher.ComputeAsync(largePath);
    Assert(largeHash.FileLength == largeFileSize, "File length mismatch");
    Assert(largeHash.FullHash != largeHash.QuickFingerprint, "For >2MB file, FullHash and QuickFingerprint must differ");
    Console.WriteLine("  ✓ PASS: Large file streaming BLAKE3 hashing verified.");

    // Test 6: Corrupt EXIF does not interrupt cataloguing; it falls back safely.
    Console.WriteLine("Running Test 6: Corrupt EXIF fallback...");
    var corruptExifPath = Path.Combine(tempDir, "corrupt_exif.jpg");
    await File.WriteAllBytesAsync(corruptExifPath,
    [
        0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x10,
        .. Encoding.ASCII.GetBytes("Exif\0\0II*\0\x08\0"),
        0xFF, 0xD9,
    ]);
    var corruptExif = await reader.ReadCaptureDateAsync(corruptExifPath);
    Assert(corruptExif.Source == DateSourceBusinessLogicModel.FilesystemMtime, "Corrupt EXIF must fall back to filesystem mtime");
    Assert(corruptExif.AllEvidence[0].Source == DateSourceBusinessLogicModel.ExifDateTimeOriginal, "EXIF evidence must be retained for corrupt metadata");
    Console.WriteLine("  ✓ PASS: Corrupt EXIF is contained and falls back safely.");

    // Test 7: XML XMP is parsed structurally, without date-looking text matching.
    Console.WriteLine("Running Test 7: XMP XML metadata...");
    var xmpPath = Path.Combine(tempDir, "sample.xmp");
    await File.WriteAllTextAsync(xmpPath, """
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmp:CreateDate="2023-04-05T06:07:08+02:00" />
          </rdf:RDF>
        </x:xmpmeta>
        """);
    var xmp = await reader.ReadCaptureDateAsync(xmpPath);
    Assert(xmp.Source == DateSourceBusinessLogicModel.Xmp, "XML XMP CreateDate must win after absent EXIF");
    Assert(xmp.Value == new DateTimeOffset(2023, 4, 5, 4, 7, 8, TimeSpan.Zero), "XMP timezone conversion mismatch");
    Console.WriteLine("  ✓ PASS: XML XMP CreateDate extracted successfully.");

    // Test 8: Version-1 QuickTime mvhd at the end of a file is discovered by the bounded scan.
    Console.WriteLine("Running Test 8: Late QuickTime mvhd v1...");
    var quickTimePath = Path.Combine(tempDir, "late-v1.mov");
    await CreateLateQuickTimeV1Async(quickTimePath, new DateTimeOffset(2022, 2, 3, 4, 5, 6, TimeSpan.Zero));
    var quickTime = await reader.ReadCaptureDateAsync(quickTimePath);
    Assert(quickTime.Source == DateSourceBusinessLogicModel.QuickTime, "Late mvhd v1 must be resolved as QuickTime");
    Assert(quickTime.Value == new DateTimeOffset(2022, 2, 3, 4, 5, 6, TimeSpan.Zero), "QuickTime v1 timestamp mismatch");
    Console.WriteLine("  ✓ PASS: Late QuickTime mvhd v1 extracted successfully.");

    // Test 9: Dependency Injection registration
    Console.WriteLine("Running Test 9: DI registration...");
    var services = new ServiceCollection();
    services.AddMediaAnalysis();
    var sp = services.BuildServiceProvider();

    var diReader = sp.GetService<IMediaMetadataReaderBusinessLogicService>();
    var diHasher = sp.GetService<IContentHasherBusinessLogicService>();
    Assert(diReader != null, "IMediaMetadataReaderBusinessLogicService must be registered");
    Assert(diHasher != null, "IContentHasherBusinessLogicService must be registered");
    Assert(diReader is MetadataExtractorMediaMetadataReaderBusinessLogicService, "Reader should be MetadataExtractorMediaMetadataReaderBusinessLogicService");
    Assert(diHasher is Blake3ContentHasherBusinessLogicService, "Hasher should be Blake3ContentHasherBusinessLogicService");
    Console.WriteLine("  ✓ PASS: Dependency Injection services correctly registered.");

    Console.WriteLine("\nALL TESTS PASSED SUCCESSFULLY! (9/9)");
    return 0;
}
finally
{
    if (Directory.Exists(tempDir))
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"ASSERTION FAILED: {message}");
    }
}

static void CreateJpegWithExif(string outputPath, string dateOriginalStr)
{
    // Build a minimal valid JPEG with an APP1 Exif segment containing DateTimeOriginal
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // 1. JPEG SOI Marker
    bw.Write((byte)0xFF);
    bw.Write((byte)0xD8);

    // 2. Prepare APP1 EXIF Payload
    using var exifMs = new MemoryStream();
    using var exifBw = new BinaryWriter(exifMs);

    // Exif Header: "Exif\0\0"
    exifBw.Write(Encoding.ASCII.GetBytes("Exif\0\0"));

    // TIFF Header (Little Endian: "II" + 42 + offset to IFD0)
    int tiffStart = (int)exifMs.Position;
    exifBw.Write((byte)'I');
    exifBw.Write((byte)'I');
    exifBw.Write((ushort)42);
    exifBw.Write((uint)8); // IFD0 offset from TIFF start

    // IFD0: 1 entry pointing to ExifSubIFD (Tag 0x8769)
    exifBw.Write((ushort)1); // entry count = 1
    // Entry 1: ExifSubIFD pointer
    exifBw.Write((ushort)0x8769); // Tag: ExifSubIFD
    exifBw.Write((ushort)4);      // Type: LONG (uint32)
    exifBw.Write((uint)1);        // Count: 1
    // Offset to SubIFD from TIFF start:
    // TIFF header (8) + IFD0 count (2) + 1 entry (12) + next IFD offset (4) = 26 bytes
    uint subIfdOffset = 26;
    exifBw.Write(subIfdOffset);
    exifBw.Write((uint)0); // Next IFD offset = 0

    // Exif SubIFD (at offset 26): 1 entry for DateTimeOriginal (Tag 0x9003)
    exifBw.Write((ushort)1); // entry count = 1
    exifBw.Write((ushort)0x9003); // Tag: DateTimeOriginal
    exifBw.Write((ushort)2);      // Type: ASCII
    var dateBytes = Encoding.ASCII.GetBytes(dateOriginalStr + "\0");
    exifBw.Write((uint)dateBytes.Length); // Count

    // Value offset from TIFF start:
    // SubIFD start (26) + count (2) + 1 entry (12) + next IFD (4) = 44
    uint valueOffset = 44;
    exifBw.Write(valueOffset);
    exifBw.Write((uint)0); // Next IFD offset = 0

    // Value data (at offset 44):
    exifBw.Write(dateBytes);

    var app1Data = exifMs.ToArray();

    // 3. Write APP1 marker & length (length includes the 2 length bytes)
    bw.Write((byte)0xFF);
    bw.Write((byte)0xE1);
    ushort app1Length = (ushort)(app1Data.Length + 2);
    bw.Write((byte)(app1Length >> 8));
    bw.Write((byte)(app1Length & 0xFF));
    bw.Write(app1Data);

    // 4. Write EOI Marker
    bw.Write((byte)0xFF);
    bw.Write((byte)0xD9);

    File.WriteAllBytes(outputPath, ms.ToArray());
}

static async Task CreateLateQuickTimeV1Async(string outputPath, DateTimeOffset timestamp)
{
    const int fillerBytes = 2 * 1024 * 1024;
    const long quickTimeEpochOffset = 2_082_844_800L;
    var seconds = checked((ulong)(timestamp.ToUnixTimeSeconds() + quickTimeEpochOffset));

    await using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
    await stream.WriteAsync(new byte[fillerBytes]);

    // A version-1 mvhd atom past 1 MiB proves scanning is not prefix-limited.
    var atom = new byte[40];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(atom.AsSpan(0, 4), 40);
    Encoding.ASCII.GetBytes("mvhd").CopyTo(atom, 4);
    atom[8] = 1;
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(atom.AsSpan(12, 8), seconds);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(atom.AsSpan(20, 8), seconds);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(atom.AsSpan(28, 4), 1_000);
    await stream.WriteAsync(atom);
}
