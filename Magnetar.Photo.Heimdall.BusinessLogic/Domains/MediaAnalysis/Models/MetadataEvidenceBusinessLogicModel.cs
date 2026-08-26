namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

// ---------------------------------------------------------------------------
// Date resolution
// ---------------------------------------------------------------------------

/// <summary>
/// Evidence collected from a single metadata source attempt.
/// A null <see cref="RawValue"/> means the source was attempted but produced no usable date.
/// A non-null <see cref="ParseError"/> means an exception was caught during extraction.
/// </summary>
public sealed record MetadataEvidenceBusinessLogicModel(
    string FilePath,
    DateSourceBusinessLogicModel? Source,
    DateTimeOffset? RawValue,
    double Confidence,
    string? ParseError);
