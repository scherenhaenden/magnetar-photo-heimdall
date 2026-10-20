namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

public sealed record LibraryDuplicateAnalysisBusinessLogicModel(
    Guid LibraryId,
    int CataloguedAssetCount,
    int CandidateFileCount,
    int HashesComputed,
    int HashesReused,
    int HashesPersisted,
    IReadOnlyList<ExactDuplicateGroupBusinessLogicModel> Groups,
    IReadOnlyList<DuplicateAnalysisIssueBusinessLogicModel> Issues);
