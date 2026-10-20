using Xunit;
using System.Collections.Concurrent;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Tests.Domains.MediaAnalysis;

public sealed class ExactDuplicateCandidateGrouperBusinessLogicServiceTests
{
    private static readonly string[] ExpectedDuplicatePaths = ["a.jpg", "b.jpg"];
    [Fact]
    public async Task AnalyzeAsync_HashesOnlyRepeatedLengths_AndGroupsOnlyMatchingFullHashes()
    {
        var hasher = new RecordingHasher(new Dictionary<string, HashEvidenceBusinessLogicModel>
        {
            ["/nas/a.jpg"] = Evidence(5, new string('a', 64)),
            ["/nas/b.jpg"] = Evidence(5, new string('a', 64)),
            ["/nas/c.jpg"] = Evidence(5, new string('b', 64))
        });
        var service = new ExactDuplicateCandidateGrouperBusinessLogicService(hasher);

        var result = await service.AnalyzeAsync(
        [
            Candidate("a.jpg", "/nas/a.jpg", 5),
            Candidate("b.jpg", "/nas/b.jpg", 5),
            Candidate("c.jpg", "/nas/c.jpg", 5),
            Candidate("unique.jpg", "/nas/unique.jpg", 19)
        ], maximumConcurrency: 2);

        Assert.Equal(3, hasher.HashedPaths.Length);
        Assert.DoesNotContain("/nas/unique.jpg", hasher.HashedPaths);
        Assert.Equal(3, result.CandidateCount);
        Assert.Equal(3, result.HashesComputed);
        var group = Assert.Single(result.Groups);
        Assert.Equal(5, group.FileLength);
        Assert.Equal("BLAKE3", group.Algorithm);
        Assert.Equal(1, group.AlgorithmVersion);
        Assert.Equal(new string('a', 64), group.FullHash);
        Assert.Equal(ExpectedDuplicatePaths, group.RelativePaths);
    }

    [Fact]
    public async Task AnalyzeAsync_ReportsStaleLengthEvidenceWithoutCreatingGroup()
    {
        var hasher = new RecordingHasher(new Dictionary<string, HashEvidenceBusinessLogicModel>
        {
            ["/nas/a.jpg"] = Evidence(4, new string('a', 64)),
            ["/nas/b.jpg"] = Evidence(5, new string('a', 64))
        });
        var service = new ExactDuplicateCandidateGrouperBusinessLogicService(hasher);

        var result = await service.AnalyzeAsync(
        [Candidate("a.jpg", "/nas/a.jpg", 5), Candidate("b.jpg", "/nas/b.jpg", 5)],
            maximumConcurrency: 1);

        Assert.Empty(result.Groups);
        Assert.Single(result.Issues);
        Assert.Equal("a.jpg", result.Issues[0].RelativePath);
    }

    [Fact]
    public async Task AnalyzeAsync_RehashesCachedEvidenceBeforeConfirmingGroup()
    {
        var hasher = new RecordingHasher(new Dictionary<string, HashEvidenceBusinessLogicModel>
        {
            ["/nas/a.jpg"] = Evidence(5, new string('a', 64)),
            ["/nas/b.jpg"] = Evidence(5, new string('a', 64))
        });
        var service = new ExactDuplicateCandidateGrouperBusinessLogicService(hasher);
        var lastWrite = DateTimeOffset.UtcNow;
        var result = await service.AnalyzeAsync(
        [
            new("a.jpg", "/nas/a.jpg", 5, lastWrite, "BLAKE3", 1, new string('a', 64)),
            new("b.jpg", "/nas/b.jpg", 5, lastWrite, "BLAKE3", 1, new string('a', 64))
        ], maximumConcurrency: 1);

        Assert.Single(result.Groups);
        Assert.Equal(2, result.HashesComputed);
        Assert.Equal(0, result.HashesReused);
        Assert.Equal(2, hasher.HashedPaths.Length);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsDuplicateRelativePaths()
    {
        var service = new ExactDuplicateCandidateGrouperBusinessLogicService(new RecordingHasher(new Dictionary<string, HashEvidenceBusinessLogicModel>()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzeAsync(
        [
            Candidate("same.jpg", "/nas/a.jpg", 5),
            Candidate("same.jpg", "/nas/b.jpg", 5)
        ], maximumConcurrency: 1));
    }

    private static HashCandidateBusinessLogicModel Candidate(string relative, string full, long length) =>
        new(relative, full, length, DateTimeOffset.UtcNow);

    private static HashEvidenceBusinessLogicModel Evidence(long length, string hash) =>
        new("BLAKE3", 1, hash, $"quick-{hash}", length);

    private sealed class RecordingHasher(IReadOnlyDictionary<string, HashEvidenceBusinessLogicModel> evidence)
        : IContentHasherBusinessLogicService
    {
        private readonly ConcurrentBag<string> _hashedPaths = [];
        public string[] HashedPaths => _hashedPaths.ToArray();

        public ValueTask<HashEvidenceBusinessLogicModel> ComputeAsync(string filePath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _hashedPaths.Add(filePath);
            if (!evidence.TryGetValue(filePath, out var hash))
                throw new IOException("Test hash mapping missing.");
            return ValueTask.FromResult(hash);
        }
    }
}
