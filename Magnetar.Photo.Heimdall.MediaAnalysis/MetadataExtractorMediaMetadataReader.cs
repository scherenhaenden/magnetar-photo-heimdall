using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using System.Buffers.Binary;

namespace Magnetar.Photo.Heimdall.MediaAnalysis;

/// <summary>
/// Resolves capture dates using the MetadataExtractor library for real EXIF/XMP/QuickTime parsing.
/// Priority chain: EXIF DateTimeOriginal → XMP → QuickTime → filesystem mtime.
/// Never throws; all parsing errors are captured in <see cref="MetadataEvidence.ParseError"/>.
/// </summary>
public sealed class MetadataExtractorMediaMetadataReader : IMediaMetadataReader
{
    // Confidence levels per source — stable constants consumed by the catalog later.
    private const double ExifConfidence      = 1.00;
    private const double XmpConfidence       = 0.90;
    private const double QuickTimeConfidence = 0.85;
    private const double MtimeConfidence     = 0.10;

    // EXIF date format as stored in TIFF/EXIF tags.
    private static readonly string[] ExifDateFormats =
    [
        "yyyy:MM:dd HH:mm:ss",
        "yyyy:MM:dd HH:mm:sszzz",
    ];

    public async ValueTask<ResolvedCaptureDate> ReadCaptureDateAsync(
        string filePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // All evidence collected (winning + rejected) for provenance tracking.
        var evidence = new List<MetadataEvidence>(4);

        IReadOnlyList<MetadataExtractor.Directory> directories;
        string? readError = null;

        try
        {
            // ImageMetadataReader works for JPEG, TIFF, PNG, HEIC, MP4/MOV, etc.
            // For unknown formats it returns an empty list rather than throwing.
            directories = await Task.Run(
                () => ImageMetadataReader.ReadMetadata(filePath), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // MetadataExtractor failed entirely — fall through to mtime.
            readError = ex.Message;
            directories = [];
        }

        ct.ThrowIfCancellationRequested();

        // ── 1. EXIF DateTimeOriginal ─────────────────────────────────────────
        var exifEvidence = TryExtractExif(filePath, directories, readError);
        evidence.Add(exifEvidence);
        if (exifEvidence.RawValue.HasValue)
        {
            return new ResolvedCaptureDate(
                exifEvidence.RawValue.Value,
                DateSource.ExifDateTimeOriginal,
                ExifConfidence,
                evidence.AsReadOnly());
        }

        // ── 2. XMP ───────────────────────────────────────────────────────────
        var xmpEvidence = TryExtractXmp(filePath, directories);
        evidence.Add(xmpEvidence);
        if (xmpEvidence.RawValue.HasValue)
        {
            return new ResolvedCaptureDate(
                xmpEvidence.RawValue.Value,
                DateSource.Xmp,
                XmpConfidence,
                evidence.AsReadOnly());
        }

        // ── 3. QuickTime mvhd ────────────────────────────────────────────────
        var qtEvidence = TryExtractQuickTime(filePath, directories);
        if (!qtEvidence.RawValue.HasValue)
        {
            var streamedQuickTime = await TryReadQuickTimeMvhdAsync(filePath, ct).ConfigureAwait(false);
            if (streamedQuickTime.HasValue)
            {
                qtEvidence = new MetadataEvidence(filePath, DateSource.QuickTime, streamedQuickTime, QuickTimeConfidence, null);
            }
        }
        evidence.Add(qtEvidence);
        if (qtEvidence.RawValue.HasValue)
        {
            return new ResolvedCaptureDate(
                qtEvidence.RawValue.Value,
                DateSource.QuickTime,
                QuickTimeConfidence,
                evidence.AsReadOnly());
        }

        // ── 4. Filesystem mtime fallback ─────────────────────────────────────
        var mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
        var mtimeEvidence = new MetadataEvidence(filePath, DateSource.FilesystemMtime, mtime, MtimeConfidence, null);
        evidence.Add(mtimeEvidence);

        return new ResolvedCaptureDate(mtime, DateSource.FilesystemMtime, MtimeConfidence, evidence.AsReadOnly());
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static MetadataEvidence TryExtractExif(
        string filePath,
        IReadOnlyList<MetadataExtractor.Directory> directories,
        string? inheritedError)
    {
        if (directories.Count == 0)
        {
            return new MetadataEvidence(filePath, DateSource.ExifDateTimeOriginal, null, ExifConfidence, inheritedError);
        }

        try
        {
            // Prefer Sub-IFD (tag DateTimeOriginal 0x9003).
            foreach (var dir in directories.OfType<ExifSubIfdDirectory>())
            {
                if (dir.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var dt))
                {
                    return new MetadataEvidence(
                        filePath, DateSource.ExifDateTimeOriginal,
                        new DateTimeOffset(dt, TimeSpan.Zero),
                        ExifConfidence, null);
                }

                // Some cameras store it as a string without a parsed DateTime.
                var raw = dir.GetString(ExifSubIfdDirectory.TagDateTimeOriginal);
                if (TryParseExifDateString(raw, out var parsed))
                {
                    return new MetadataEvidence(
                        filePath, DateSource.ExifDateTimeOriginal,
                        parsed, ExifConfidence, null);
                }
            }

            // Fallback: IFD0 DateTime tag (0x0132).
            foreach (var dir in directories.OfType<ExifIfd0Directory>())
            {
                if (dir.TryGetDateTime(ExifIfd0Directory.TagDateTime, out var dt))
                {
                    return new MetadataEvidence(
                        filePath, DateSource.ExifDateTimeOriginal,
                        new DateTimeOffset(dt, TimeSpan.Zero),
                        ExifConfidence, null);
                }
            }
        }
        catch (Exception ex)
        {
            return new MetadataEvidence(filePath, DateSource.ExifDateTimeOriginal, null, ExifConfidence, ex.Message);
        }

        return new MetadataEvidence(filePath, DateSource.ExifDateTimeOriginal, null, ExifConfidence, null);
    }

    private static MetadataEvidence TryExtractXmp(
        string filePath,
        IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        try
        {
            foreach (var dir in directories.OfType<XmpDirectory>())
            {
                var xmp = dir.XmpMeta;
                if (xmp is null) continue;

                // Try the most common XMP date properties in priority order.
                string?[] propertyPaths =
                [
                    "http://ns.adobe.com/xap/1.0/|CreateDate",
                    "http://ns.adobe.com/photoshop/1.0/|DateCreated",
                    "http://ns.adobe.com/exif/1.0/|DateTimeOriginal",
                ];

                foreach (var prop in propertyPaths)
                {
                    var parts = prop!.Split('|');
                    try
                    {
                        var propValue = xmp.GetPropertyString(parts[0], parts[1]);
                        if (!string.IsNullOrWhiteSpace(propValue) &&
                            DateTimeOffset.TryParse(propValue, CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out var dto))
                        {
                            return new MetadataEvidence(filePath, DateSource.Xmp, dto, XmpConfidence, null);
                        }
                    }
                    catch (XmpCore.XmpException)
                    {
                        // Property doesn't exist in this XMP block — try next.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return new MetadataEvidence(filePath, DateSource.Xmp, null, XmpConfidence, ex.Message);
        }

        // MetadataExtractor only sees XMP embedded in formats it recognizes. Also accept a
        // standalone XMP packet, parsing XML rather than matching date-looking text.
        try
        {
            var embedded = TryReadXmpPacket(filePath);
            if (embedded.HasValue)
            {
                return new MetadataEvidence(filePath, DateSource.Xmp, embedded, XmpConfidence, null);
            }
        }
        catch (Exception ex)
        {
            return new MetadataEvidence(filePath, DateSource.Xmp, null, XmpConfidence, ex.Message);
        }

        return new MetadataEvidence(filePath, DateSource.Xmp, null, XmpConfidence, null);
    }

    private static DateTimeOffset? TryReadXmpPacket(string filePath)
    {
        const int maximumPacketBytes = 16 * 1024 * 1024;
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[(int)Math.Min(stream.Length, maximumPacketBytes)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) break;
            offset += read;
        }
        var text = Encoding.UTF8.GetString(bytes, 0, offset);
        var start = text.IndexOf("<x:xmpmeta", StringComparison.OrdinalIgnoreCase);
        var end = text.IndexOf("</x:xmpmeta>", start < 0 ? 0 : start, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end < start) return null;
        var packet = XDocument.Parse(text[start..(end + "</x:xmpmeta>".Length)]);
        foreach (var element in packet.Root!.DescendantsAndSelf())
        {
            var candidates = element.Attributes().Select(a => (Name: a.Name.LocalName, Value: a.Value))
                .Append((Name: element.Name.LocalName, Value: element.Value));
            foreach (var candidate in candidates)
            {
                if (candidate.Name is not ("CreateDate" or "DateCreated" or "DateTimeOriginal")) continue;
                if (DateTimeOffset.TryParse(candidate.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)) return date;
            }
        }
        return null;
    }

    private static MetadataEvidence TryExtractQuickTime(
        string filePath,
        IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        try
        {
            foreach (var dir in directories.OfType<QuickTimeMovieHeaderDirectory>())
            {
                if (dir.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var dt))
                {
                    return new MetadataEvidence(
                        filePath, DateSource.QuickTime,
                        new DateTimeOffset(dt, TimeSpan.Zero),
                        QuickTimeConfidence, null);
                }
            }
        }
        catch (Exception ex)
        {
            return new MetadataEvidence(filePath, DateSource.QuickTime, null, QuickTimeConfidence, ex.Message);
        }

        return new MetadataEvidence(filePath, DateSource.QuickTime, null, QuickTimeConfidence, null);
    }

    // MetadataExtractor is preferred for structured files; this bounded-memory scan covers
    // late moov atoms and mvhd v1 timestamps without imposing an arbitrary file-size limit.
    private static async Task<DateTimeOffset?> TryReadQuickTimeMvhdAsync(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024 + 7];
        var carry = 0;
        long absolute = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(carry, 64 * 1024), ct).ConfigureAwait(false);
            if (read == 0) return null;
            var total = carry + read;
            for (var index = 4; index + 4 <= total; index++)
            {
                if (!buffer.AsSpan(index, 4).SequenceEqual("mvhd"u8)) continue;
                var parsed = await TryParseMvhdAsync(stream, absolute - carry + index - 4, ct).ConfigureAwait(false);
                if (parsed.HasValue) return parsed;
                stream.Position = absolute + read;
            }
            carry = Math.Min(7, total);
            buffer.AsSpan(total - carry, carry).CopyTo(buffer);
            absolute += read;
        }
    }

    private static async Task<DateTimeOffset?> TryParseMvhdAsync(FileStream stream, long atomOffset, CancellationToken ct)
    {
        if (atomOffset < 0 || atomOffset > stream.Length - 20) return null;
        var header = new byte[32];
        stream.Position = atomOffset;
        var read = await stream.ReadAsync(header, ct).ConfigureAwait(false);
        if (read < 20 || !header.AsSpan(4, 4).SequenceEqual("mvhd"u8)) return null;
        var size32 = BinaryPrimitives.ReadUInt32BigEndian(header);
        var headerSize = size32 == 1 ? 16 : 8;
        var atomSize = size32 == 1 && read >= 16 ? (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8)) : size32;
        if (atomSize < headerSize + 12 || atomSize > stream.Length - atomOffset) return null;
        var version = header[headerSize];
        var creationOffset = version switch { 0 => headerSize + 4, 1 => headerSize + 4, _ => -1 };
        var required = version == 0 ? creationOffset + 4 : creationOffset + 8;
        if (creationOffset < 0 || atomSize < required || read < required) return null;
        var seconds = version == 0 ? BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(creationOffset, 4)) : (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(creationOffset, 8));
        try { return DateTimeOffset.UnixEpoch.AddSeconds(seconds - 2_082_844_800L); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static bool TryParseExifDateString(string? text, out DateTimeOffset value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        return DateTimeOffset.TryParseExact(
            text,
            ExifDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }
}
