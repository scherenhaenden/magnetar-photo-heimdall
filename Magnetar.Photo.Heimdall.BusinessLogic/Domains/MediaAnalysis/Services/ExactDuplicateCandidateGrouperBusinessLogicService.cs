using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;

/// <summary>
/// Filters by file length before reading content, then confirms equality with a
/// complete BLAKE3 hash. It performs no file mutations.
/// </summary>
public sealed class ExactDuplicateCandidateGrouperBusinessLogicService(
    IContentHasherBusinessLogicService contentHasher)
{
    public async Task<ExactDuplicateCandidateAnalysisBusinessLogicModel> AnalyzeAsync(
        IReadOnlyCollection<HashCandidateBusinessLogicModel> candidates,
        int maximumConcurrency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        if (candidates.Any(candidate => candidate.FileLength < 0))
            throw new ArgumentException("Candidate file lengths cannot be negative.", nameof(candidates));
        if (candidates.Any(candidate => string.IsNullOrWhiteSpace(candidate.RelativePath) || string.IsNullOrWhiteSpace(candidate.FullPath)))
            throw new ArgumentException("Candidate paths must be non-empty.", nameof(candidates));
        if (candidates.Select(candidate => candidate.RelativePath).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new ArgumentException("Candidate relative paths must be unique.", nameof(candidates));

        var repeatedLengthCandidates = candidates
            .GroupBy(candidate => candidate.FileLength)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToArray();
        var evidence = new HashCandidateEvidenceBusinessLogicModel?[repeatedLengthCandidates.Length];
        var issues = new DuplicateAnalysisIssueBusinessLogicModel?[repeatedLengthCandidates.Length];
        var hashesComputed = 0;
        var hashesReused = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, repeatedLengthCandidates.Length),
            new ParallelOptions { MaxDegreeOfParallelism = maximumConcurrency, CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                var candidate = repeatedLengthCandidates[index];
                // Size and mtime are not a stable content identity: a replacement can preserve both.
                // Until the catalog persists a trustworthy inode/file-id generation, rehash candidates
                // on every new analysis to keep stale SQLite evidence out of confirmed groups.
                try
                {
                    var hash = await contentHasher.ComputeAsync(candidate.FullPath, ct).ConfigureAwait(false);
                    if (hash.FileLength != candidate.FileLength)
                        throw new IOException("File length changed while it was being hashed.");
                    if (!StringComparer.Ordinal.Equals(hash.Algorithm, "BLAKE3"))
                        throw new InvalidOperationException($"Unexpected content hash algorithm: {hash.Algorithm}");
                    if (hash.FullHash.Length != 64 || !hash.FullHash.All(Uri.IsHexDigit))
                        throw new InvalidDataException("BLAKE3 evidence must be a complete 256-bit hexadecimal digest.");

                    evidence[index] = new HashCandidateEvidenceBusinessLogicModel(
                        candidate.RelativePath, candidate.FileLength, candidate.LastWriteUtc,
                        hash.Algorithm, Blake3ContentHasherBusinessLogicService.FullHashVersion,
                        hash.FullHash.ToLowerInvariant(), WasComputed: true);
                    Interlocked.Increment(ref hashesComputed);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    issues[index] = new DuplicateAnalysisIssueBusinessLogicModel(candidate.RelativePath, exception.Message);
                }
            }).ConfigureAwait(false);

        var validEvidence = evidence.Where(item => item is not null).Select(item => item!).ToArray();
        var groups = validEvidence
            .GroupBy(item => (item.FileLength, item.Algorithm, item.AlgorithmVersion, item.FullHash))
            .Where(group => group.Count() > 1)
            .Select(group => new ExactDuplicateGroupBusinessLogicModel(
                group.Key.FileLength,
                group.Key.Algorithm,
                group.Key.AlgorithmVersion,
                group.Key.FullHash,
                group.Select(item => item.RelativePath).Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(group => group.FileLength)
            .ThenBy(group => group.FullHash, StringComparer.Ordinal)
            .ToArray();

        return new ExactDuplicateCandidateAnalysisBusinessLogicModel(
            groups,
            validEvidence,
            issues.Where(item => item is not null).Select(item => item!).ToArray(),
            repeatedLengthCandidates.Length,
            hashesComputed,
            hashesReused);
    }
}
