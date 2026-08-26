namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

/// <summary>
/// Reads capture-date metadata from a media file and returns a fully-provenance-tracked result.
/// Implementations must never throw; errors are captured inside <see cref="MetadataEvidenceBusinessLogicModel"/>.
/// </summary>
public interface IMediaMetadataReaderBusinessLogicService
{
    /// <summary>
    /// Resolves the capture date for <paramref name="filePath"/>.
    /// Never throws. Falls back to filesystem mtime when all parsers fail.
    /// </summary>
    ValueTask<ResolvedCaptureDateBusinessLogicModel> ReadCaptureDateAsync(
        string filePath,
        CancellationToken ct = default);
}