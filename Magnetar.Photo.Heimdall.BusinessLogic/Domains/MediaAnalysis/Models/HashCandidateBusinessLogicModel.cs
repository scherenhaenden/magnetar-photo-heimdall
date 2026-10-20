namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

/// <summary>A root-resolved file discovered by a trusted local scan.</summary>
public sealed record HashCandidateBusinessLogicModel(
    string RelativePath,
    string FullPath,
    long FileLength,
    DateTimeOffset LastWriteUtc,
    string? CachedHashAlgorithm = null,
    int? CachedHashAlgorithmVersion = null,
    string? CachedFullHash = null);
