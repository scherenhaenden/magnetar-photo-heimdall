namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

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