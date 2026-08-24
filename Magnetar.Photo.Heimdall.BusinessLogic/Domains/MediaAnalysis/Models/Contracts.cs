namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

// ---------------------------------------------------------------------------
// Date resolution
// ---------------------------------------------------------------------------

/// <summary>Priority-ordered sources for a media file's capture date.</summary>
public enum DateSource
{
    /// <summary>EXIF tag DateTimeOriginal (0x9003) from the Exif Sub-IFD.</summary>
    ExifDateTimeOriginal = 0,

    /// <summary>XMP property (xmp:CreateDate / photoshop:DateCreated / exif:DateTimeOriginal).</summary>
    Xmp = 1,

    /// <summary>QuickTime movie-header atom (mvhd) creation time.</summary>
    QuickTime = 2,

    /// <summary>Filesystem last-write time — lowest confidence fallback.</summary>
    FilesystemMtime = 3,
}

/// <summary>
/// Evidence collected from a single metadata source attempt.
/// A null <see cref="RawValue"/> means the source was attempted but produced no usable date.
/// A non-null <see cref="ParseError"/> means an exception was caught during extraction.
/// </summary>
public sealed record MetadataEvidence(
    string FilePath,
    DateSource? Source,
    DateTimeOffset? RawValue,
    double Confidence,
    string? ParseError);

/// <summary>
/// Final resolved capture date, with its winning source, confidence score,
/// and the complete list of all evidence (winning + rejected).
/// </summary>
public sealed record ResolvedCaptureDate(
    DateTimeOffset Value,
    DateSource Source,
    double Confidence,
    IReadOnlyList<MetadataEvidence> AllEvidence);

// ---------------------------------------------------------------------------
// Content hashing
// ---------------------------------------------------------------------------

/// <summary>
/// The result of content hashing: a full digest and a versioned quick fingerprint.
/// </summary>
/// <param name="Algorithm">Always "BLAKE3".</param>
/// <param name="Version">
/// Fingerprint scheme version. Version 1 = BLAKE3(Length_LE8 || first_1MiB || last_1MiB).
/// </param>
/// <param name="FullHash">Lowercase hex of the BLAKE3 hash of the complete file.</param>
/// <param name="QuickFingerprint">
/// Lowercase hex of the versioned quick fingerprint.
/// Deterministic for the same file content; changes whenever any byte changes in the sampled windows.
/// </param>
/// <param name="FileLength">File size in bytes at the time of hashing.</param>
public sealed record HashEvidence(
    string Algorithm,
    int QuickFingerprintVersion,
    string FullHash,
    string QuickFingerprint,
    long FileLength);

// ---------------------------------------------------------------------------
// Service contracts
// ---------------------------------------------------------------------------

/// <summary>
/// Reads capture-date metadata from a media file and returns a fully-provenance-tracked result.
/// Implementations must never throw; errors are captured inside <see cref="MetadataEvidence"/>.
/// </summary>
public interface IMediaMetadataReader
{
    /// <summary>
    /// Resolves the capture date for <paramref name="filePath"/>.
    /// Never throws. Falls back to filesystem mtime when all parsers fail.
    /// </summary>
    ValueTask<ResolvedCaptureDate> ReadCaptureDateAsync(
        string filePath,
        CancellationToken ct = default);
}

/// <summary>
/// Computes BLAKE3 content hashes for a media file using streaming I/O.
/// No file is loaded entirely into memory.
/// </summary>
public interface IContentHasher
{
    /// <summary>
    /// Computes the full BLAKE3 hash and a versioned quick fingerprint for <paramref name="filePath"/>.
    /// </summary>
    ValueTask<HashEvidence> ComputeAsync(
        string filePath,
        CancellationToken ct = default);
}
