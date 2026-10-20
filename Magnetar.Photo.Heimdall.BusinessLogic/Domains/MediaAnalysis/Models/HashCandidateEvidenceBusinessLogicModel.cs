namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

public sealed record HashCandidateEvidenceBusinessLogicModel(
    string RelativePath,
    long FileLength,
    DateTimeOffset LastWriteUtc,
    string Algorithm,
    int AlgorithmVersion,
    string FullHash,
    bool WasComputed);
