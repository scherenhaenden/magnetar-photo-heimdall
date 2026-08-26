namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

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