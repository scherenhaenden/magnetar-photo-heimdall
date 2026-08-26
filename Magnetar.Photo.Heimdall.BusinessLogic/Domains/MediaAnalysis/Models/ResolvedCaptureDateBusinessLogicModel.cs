namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

/// <summary>
/// Final resolved capture date, with its winning source, confidence score,
/// and the complete list of all evidence (winning + rejected).
/// </summary>
public sealed record ResolvedCaptureDateBusinessLogicModel(
    DateTimeOffset Value,
    DateSourceBusinessLogicModel Source,
    double Confidence,
    IReadOnlyList<MetadataEvidenceBusinessLogicModel> AllEvidence);