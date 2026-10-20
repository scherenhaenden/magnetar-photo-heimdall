namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

public sealed record ExactDuplicateCandidateAnalysisBusinessLogicModel(
    IReadOnlyList<ExactDuplicateGroupBusinessLogicModel> Groups,
    IReadOnlyList<HashCandidateEvidenceBusinessLogicModel> Evidence,
    IReadOnlyList<DuplicateAnalysisIssueBusinessLogicModel> Issues,
    int CandidateCount,
    int HashesComputed,
    int HashesReused);
