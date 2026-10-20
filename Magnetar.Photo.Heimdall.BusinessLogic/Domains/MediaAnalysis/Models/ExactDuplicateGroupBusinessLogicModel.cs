namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

/// <summary>A group of files with matching length and complete versioned content hash.</summary>
public sealed record ExactDuplicateGroupBusinessLogicModel(
    long FileLength,
    string Algorithm,
    int AlgorithmVersion,
    string FullHash,
    IReadOnlyList<string> RelativePaths);
