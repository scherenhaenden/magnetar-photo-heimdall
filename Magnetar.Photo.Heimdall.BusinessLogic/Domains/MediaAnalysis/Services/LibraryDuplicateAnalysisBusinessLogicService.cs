using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using System.Security;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;

/// <summary>
/// Scans one already-authorized library root, hashes only repeated-size assets,
/// persists versioned BLAKE3 evidence in SQLite, and returns confirmed groups.
/// Instantiate this service in the process that can access the library files
/// (the NAS agent for remote libraries), never in the desktop against an NFS mount.
/// </summary>
public sealed class LibraryDuplicateAnalysisBusinessLogicService(
    ILibraryCatalogDataAccessService catalog,
    ExactDuplicateCandidateGrouperBusinessLogicService grouper)
{
    public async Task<LibraryDuplicateAnalysisBusinessLogicModel> AnalyzeAsync(
        LibraryRootDataAccessModel library,
        int maximumConcurrency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        if (!Directory.Exists(library.CanonicalPath))
            throw new DirectoryNotFoundException("The configured library root is not available to this worker.");

        var root = Path.GetFullPath(library.CanonicalPath);
        await catalog.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await catalog.ScanLibraryAsync(library, cancellationToken).ConfigureAwait(false);
        var assetCount = await catalog.CountAssetsAsync(library.Id, cancellationToken).ConfigureAwait(false);
        // Read and process one repeated-size bucket at a time. Memory is bounded by
        // the largest same-size bucket instead of all duplicate candidates in the library.
        const int SizePageLength = 128;
        long? afterLength = null;
        var candidateCount = 0;
        var hashesComputed = 0;
        var hashesReused = 0;
        var hashesPersisted = 0;
        var groups = new List<ExactDuplicateGroupBusinessLogicModel>();
        var issues = new List<DuplicateAnalysisIssueBusinessLogicModel>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sizePage = await catalog.ListRepeatedSizeValuesAsync(library.Id, afterLength, SizePageLength, cancellationToken).ConfigureAwait(false);
            if (sizePage.Count == 0) break;
            foreach (var size in sizePage)
            {
                var sizeAssets = await catalog.ListAssetsBySizeAsync(library.Id, size, cancellationToken).ConfigureAwait(false);
                var bucketCandidates = new List<HashCandidateBusinessLogicModel>(sizeAssets.Count);
                foreach (var asset in sizeAssets)
                {
                    string? reason = null;
                    string fullPath = string.Empty;
                    var candidateIsCurrent = false;
                    try
                    {
                        candidateIsCurrent = TryResolveCandidate(root, asset.RelativePath, out fullPath, out reason) &&
                                             MatchesCatalogIdentity(fullPath, asset.Length, asset.LastWriteUtc);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        reason = exception.Message;
                    }
                    if (!candidateIsCurrent)
                    {
                        issues.Add(new DuplicateAnalysisIssueBusinessLogicModel(asset.RelativePath,
                            reason ?? "The file changed or disappeared after the catalog scan."));
                        continue;
                    }
                    bucketCandidates.Add(new HashCandidateBusinessLogicModel(
                        asset.RelativePath, fullPath, asset.Length, asset.LastWriteUtc,
                        asset.HashAlgorithm, asset.HashAlgorithmVersion, asset.FullHash));
                }
                candidateCount += bucketCandidates.Count;
                if (bucketCandidates.Count < 2) continue;

                var analysis = await grouper.AnalyzeAsync(bucketCandidates, maximumConcurrency, cancellationToken).ConfigureAwait(false);
                hashesComputed += analysis.HashesComputed;
                hashesReused += analysis.HashesReused;
                issues.AddRange(analysis.Issues);
                var candidatesByPath = bucketCandidates.ToDictionary(candidate => candidate.RelativePath, StringComparer.Ordinal);
                var verifiedPaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var evidence in analysis.Evidence)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = candidatesByPath[evidence.RelativePath];
                    if (!MatchesCatalogIdentity(candidate.FullPath, candidate.FileLength, candidate.LastWriteUtc))
                    {
                        issues.Add(new DuplicateAnalysisIssueBusinessLogicModel(candidate.RelativePath,
                            "The file changed while duplicate analysis was running."));
                        continue;
                    }
                    var saved = await catalog.SaveHashEvidenceAsync(
                        library.Id, candidate.RelativePath, candidate.FileLength, candidate.LastWriteUtc,
                        evidence.Algorithm, evidence.AlgorithmVersion, evidence.FullHash, cancellationToken).ConfigureAwait(false);
                    if (!saved)
                    {
                        issues.Add(new DuplicateAnalysisIssueBusinessLogicModel(candidate.RelativePath,
                            "SQLite rejected stale hash evidence because the catalog identity changed."));
                        continue;
                    }
                    hashesPersisted++;
                    verifiedPaths.Add(candidate.RelativePath);
                }
                groups.AddRange(analysis.Groups
                    .Select(group => new ExactDuplicateGroupBusinessLogicModel(
                        group.FileLength, group.Algorithm, group.AlgorithmVersion, group.FullHash,
                        group.RelativePaths.Where(verifiedPaths.Contains).Order(StringComparer.Ordinal).ToArray()))
                    .Where(group => group.RelativePaths.Count > 1));
            }
            afterLength = sizePage[^1];
        }

        return new LibraryDuplicateAnalysisBusinessLogicModel(
            library.Id,
            assetCount,
            candidateCount,
            hashesComputed,
            hashesReused,
            hashesPersisted,
            groups,
            issues);
    }

    private static bool MatchesCatalogIdentity(string path, long expectedLength, DateTimeOffset expectedLastWriteUtc)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;
        return info.Length == expectedLength &&
               new DateTimeOffset(info.LastWriteTimeUtc) == expectedLastWriteUtc;
    }

    private static bool TryResolveCandidate(string root, string relativePath, out string fullPath, out string? reason)
    {
        fullPath = string.Empty;
        reason = null;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') || relativePath.Contains('\0'))
        {
            reason = "The catalog contains a non-relative or malformed path.";
            return false;
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            reason = "The catalog path contains an invalid path segment.";
            return false;
        }

        var rootPath = Path.GetFullPath(root);
        var rootPrefix = Path.EndsInDirectorySeparator(rootPath) ? rootPath : rootPath + Path.DirectorySeparatorChar;
        fullPath = Path.GetFullPath(Path.Combine(rootPath, Path.Combine(segments)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            reason = "The catalog path resolves outside the configured library root.";
            return false;
        }

        var current = rootPath;
        try
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                reason = "The configured library root is a symbolic link or reparse point.";
                return false;
            }
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    reason = "The catalog path traverses a symbolic link or reparse point.";
                    return false;
                }
            }
        }
        catch (FileNotFoundException)
        {
            reason = "The catalogued file no longer exists.";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            reason = "A directory in the catalogued path no longer exists.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "The worker cannot inspect the catalogued path.";
            return false;
        }
        return true;
    }
}
