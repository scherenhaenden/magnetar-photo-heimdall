namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

/// <summary>
/// Final resolved capture date, with its winning source, confidence score,
/// and the complete list of all evidence (winning + rejected).
/// </summary>
public sealed record ResolvedCaptureDate(
    DateTimeOffset Value,
    DateSource Source,
    double Confidence,
    IReadOnlyList<MetadataEvidence> AllEvidence);