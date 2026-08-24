using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;
using System.Globalization;

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

        return new MetadataEvidence(filePath, DateSource.Xmp, null, XmpConfidence, null);
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
