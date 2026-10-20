using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Services;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Models;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;
using Microsoft.Data.Sqlite;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Host;

/// <summary>NAS-local, allowlisted-library consolidation. It never accepts arbitrary filesystem paths.</summary>
internal sealed class ConsolidationRemoteService(
    IReadOnlyDictionary<string, (AgentRootConfiguration Config, string CanonicalPath)> roots,
    string journalPath)
{
    private const string QuarantineDirectoryName = ".heimdall-quarantine";
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    private readonly Blake3ContentHasherBusinessLogicService _hasher = new();
    private readonly MetadataExtractorMediaMetadataReaderBusinessLogicService _metadataReader = new();
    private readonly OrganizerPathPlannerBusinessLogicService _pathPlanner = new();

    public async Task<ConsolidationResultRemoteContractsModel> RunAsync(
        OperationRequestRemoteContractsModel request,
        string phase,
        CancellationToken cancellationToken = default)
    {
        var approvedPlan = phase == "plan" ? null : await LoadApprovedPlanAsync(request, phase, cancellationToken);
        var selectedPlanItems = approvedPlan?.Items.Where(item => request.SelectedPlanItemIds!.Contains(item.PlanItemId, StringComparer.Ordinal))
            .ToDictionary(item => item.PlanItemId, StringComparer.Ordinal);
        var destinationRoot = roots[request.RootId].CanonicalPath;
        var sourceRoots = request.SourceRootIds!.ToDictionary(id => id, id => roots[id].CanonicalPath, StringComparer.Ordinal);
        var all = new List<Entry>();
        foreach (var (rootId, path) in sourceRoots)
            all.AddRange(await EnumerateAsync(rootId, path, isDestination: false, cancellationToken));
        all.AddRange(await EnumerateAsync(request.RootId, destinationRoot, isDestination: true, cancellationToken));

        // Size is the cheap first-stage key. BLAKE3 reads full content only when another file has
        // the same size somewhere in the participating source/destination roots.
        var sizeGroups = all.GroupBy(entry => entry.Model.Length).Where(group => group.Count() > 1).ToArray();
        var hashesComputed = 0;
        foreach (var group in sizeGroups)
        {
            foreach (var entry in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Digest is null)
                {
                    entry.Digest = (await _hasher.ComputeAsync(entry.Model.FullPath, cancellationToken)).FullHash;
                    hashesComputed++;
                    await PersistHashAsync(entry, cancellationToken);
                }
            }
        }

        // A plan is a promise about exact source bytes. Even a singleton size bucket needs a
        // complete digest so execution can reject a changed source after the preview.
        foreach (var source in all.Where(entry => !entry.IsDestination && entry.Digest is null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.Digest = (await _hasher.ComputeAsync(source.Model.FullPath, cancellationToken)).FullHash;
            hashesComputed++;
            await PersistHashAsync(source, cancellationToken);
        }

        var destinationByDigest = all.Where(entry => entry.IsDestination && entry.Digest is not null)
            .GroupBy(entry => entry.Digest!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var destinationByPath = all.Where(entry => entry.IsDestination)
            .ToDictionary(entry => entry.Model.RelativePath, StringComparer.Ordinal);
        var sourceEntries = all.Where(entry => !entry.IsDestination).ToArray();
        var issues = new List<string>();
        var items = new List<ConsolidationPlanItemRemoteContractsModel>();
        var copied = 0;
        var present = 0;
        var quarantined = 0;
        var representativeDigests = new HashSet<string>(StringComparer.Ordinal);
        var seenSelectedPlanItems = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sourceEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceItemId = GetPlanItemId(source.RootId, source.Model.RelativePath);
            if (phase != "plan" && !selectedPlanItems!.ContainsKey(sourceItemId)) continue;
            if (phase != "plan") seenSelectedPlanItems.Add(sourceItemId);
            var approvedItem = phase == "plan" ? null : selectedPlanItems![sourceItemId];
            if (phase != "plan" && (approvedItem!.Length != source.Model.Length || approvedItem.LastWriteUtc != source.Model.LastWriteUtc.ToUniversalTime() ||
                !StringComparer.Ordinal.Equals(approvedItem.Blake3, source.Digest)))
            {
                issues.Add($"Plan is stale for source '{source.RootId}/{source.Model.RelativePath}'; no action was taken.");
                items.Add(Item(source, "PlanStale", approvedItem.DestinationRelativePath, "Source length, timestamp, or BLAKE3 differs from the preview."));
                continue;
            }
            if (phase == "copy" && approvedItem!.Action != "CopyRequired")
            {
                issues.Add($"Plan item '{sourceItemId}' is not approved for copying.");
                continue;
            }
            if (phase == "quarantine" && approvedItem!.Action is not ("AlreadyInDestination" or "CopyRequired" or "DuplicateSource"))
            {
                issues.Add($"Plan item '{sourceItemId}' is not eligible for quarantine.");
                continue;
            }
            if (source.Digest is null && phase == "copy")
            {
                source.Digest = (await _hasher.ComputeAsync(source.Model.FullPath, cancellationToken)).FullHash;
                hashesComputed++;
                await PersistHashAsync(source, cancellationToken);
            }
            if (source.Digest is null)
            {
                if (phase == "plan")
                {
                    var plannedPath = await PlanDestinationPathAsync(source, request.OrganizerProfile!, source.Digest, cancellationToken);
                    items.Add(Item(source, "CopyRequired", plannedPath, "No participating file has the same size; content cannot already exist in a participating root."));
                    continue;
                }
                items.Add(Item(source, "KeptSource", null, "No digest evidence is available for this action."));
                continue;
            }

            var matchingDestination = destinationByDigest.TryGetValue(source.Digest, out var matches) ? matches.FirstOrDefault() : null;
            var plannedRelativePath = approvedItem?.DestinationRelativePath ?? await PlanDestinationPathAsync(source, request.OrganizerProfile!, source.Digest, cancellationToken);
            if (phase == "quarantine")
            {
                if (matchingDestination is null)
                {
                    items.Add(Item(source, "KeptSource", null, "No verified matching file exists in the destination."));
                    continue;
                }
                var actualDestinationHash = (await _hasher.ComputeAsync(matchingDestination.Model.FullPath, cancellationToken)).FullHash;
                if (!StringComparer.Ordinal.Equals(actualDestinationHash, source.Digest))
                {
                    issues.Add($"Destination verification failed for source '{source.RootId}/{source.Model.RelativePath}'.");
                    items.Add(Item(source, "VerificationFailed", null, "Destination content changed; source was kept."));
                    continue;
                }
                var sourceHashAtMove = (await _hasher.ComputeAsync(source.Model.FullPath, cancellationToken)).FullHash;
                if (!StringComparer.Ordinal.Equals(sourceHashAtMove, source.Digest))
                {
                    issues.Add($"Source verification failed before quarantine for '{source.RootId}/{source.Model.RelativePath}'.");
                    items.Add(Item(source, "VerificationFailed", null, "Source content changed; source was kept."));
                    continue;
                }
                var quarantinePath = Path.Combine(source.RootPath, QuarantineDirectoryName, request.IdempotencyKey, source.Model.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!IsContained(source.RootPath, quarantinePath))
                    throw new InvalidDataException("Computed quarantine path escaped its configured source root.");
                CreateDirectoriesSafely(source.RootPath, Path.GetDirectoryName(quarantinePath)!);
                if (File.Exists(quarantinePath))
                    throw new IOException("Quarantine target already exists; source was not moved.");
                File.Move(source.Model.FullPath, quarantinePath);
                quarantined++;
                items.Add(Item(source, "Quarantined", null, "Moved within the NAS to reversible quarantine after destination verification."));
                continue;
            }

            if (matchingDestination is not null)
            {
                present++;
                items.Add(Item(source, "AlreadyInDestination", matchingDestination.Model.RelativePath));
                continue;
            }

            if (!representativeDigests.Add(source.Digest))
            {
                items.Add(Item(source, "DuplicateSource", null, "Another source file with identical content is the copy representative."));
                continue;
            }

            if (destinationByPath.TryGetValue(plannedRelativePath, out var pathCollision))
            {
                issues.Add($"Destination path '{plannedRelativePath}' is occupied by different content; source was not copied.");
                items.Add(Item(source, "PathConflict", plannedRelativePath, "Destination path contains different bytes."));
                continue;
            }

            if (phase == "copy")
            {
                var targetPath = Path.Combine(destinationRoot, plannedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!IsContained(destinationRoot, targetPath)) throw new InvalidDataException("Computed destination path escaped its configured root.");
                CreateDirectoriesSafely(destinationRoot, Path.GetDirectoryName(targetPath)!);
                if (File.Exists(targetPath))
                {
                    issues.Add($"Destination path '{plannedRelativePath}' appeared during copy; source was kept.");
                    items.Add(Item(source, "PathConflict", plannedRelativePath, "Destination path appeared during execution."));
                    continue;
                }
                var stageSuffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.IdempotencyKey)))[..16].ToLowerInvariant();
                var stagedPath = targetPath + ".heimdall-staging-" + stageSuffix;
                var stageCreated = false;
                string copiedHash;
                try
                {
                    await using (var input = new FileStream(source.Model.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await using (var output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        stageCreated = true;
                        await input.CopyToAsync(output, cancellationToken);
                        await output.FlushAsync(cancellationToken);
                    }
                    copiedHash = (await _hasher.ComputeAsync(stagedPath, cancellationToken)).FullHash;
                    if (!StringComparer.Ordinal.Equals(copiedHash, source.Digest))
                    {
                        issues.Add($"BLAKE3 verification failed after staging '{source.RootId}/{source.Model.RelativePath}'.");
                        items.Add(Item(source, "VerificationFailed", plannedRelativePath, "Unverified stage was removed; source was kept."));
                        continue;
                    }
                    if (File.Exists(targetPath))
                    {
                        issues.Add($"Destination path '{plannedRelativePath}' appeared during copy; source was kept.");
                        items.Add(Item(source, "PathConflict", plannedRelativePath, "Destination path appeared during execution."));
                        continue;
                    }
                    File.Move(stagedPath, targetPath);
                    stageCreated = false;
                }
                finally
                {
                    if (stageCreated && File.Exists(stagedPath)) File.Delete(stagedPath);
                }
                var finalHash = (await _hasher.ComputeAsync(targetPath, cancellationToken)).FullHash;
                if (!StringComparer.Ordinal.Equals(finalHash, source.Digest))
                {
                    File.Delete(targetPath); // only the just-created, unverified destination file
                    issues.Add($"BLAKE3 verification failed after copying '{source.RootId}/{source.Model.RelativePath}'.");
                    items.Add(Item(source, "VerificationFailed", plannedRelativePath, "Unverified destination copy was removed; source was kept."));
                    continue;
                }
                copied++;
                var copiedEntry = new Entry(request.RootId, destinationRoot, source.Model with { FullPath = targetPath }, true) { Digest = copiedHash };
                destinationByDigest[copiedHash] = [copiedEntry];
                destinationByPath[plannedRelativePath] = copiedEntry;
                items.Add(Item(source, "CopiedAndVerified", plannedRelativePath));
            }
            else
            {
                items.Add(Item(source, "CopyRequired", plannedRelativePath));
            }
        }

        if (phase != "plan")
        {
            foreach (var (itemId, approvedItem) in selectedPlanItems!)
            {
                if (seenSelectedPlanItems.Contains(itemId)) continue;
                issues.Add($"Planned source '{approvedItem.SourceRootId}/{approvedItem.RelativePath}' is missing; no action was taken.");
                items.Add(approvedItem with { Action = "PlanStale", Reason = "Source is missing from the configured root." });
            }
        }

        var result = new ConsolidationResultRemoteContractsModel(phase == "plan" ? request.IdempotencyKey : request.PlanId!, request.RootId, request.SourceRootIds!,
            phase == "plan" ? "Preview" : phase == "copy" ? "Copy" : "Quarantine",
            all.Count, hashesComputed, copied, present, quarantined, items, issues, request.OrganizerProfile);
        await PersistRunAsync(request, result, cancellationToken);
        return result;
    }

    private static async Task<List<Entry>> EnumerateAsync(string rootId, string path, bool isDestination, CancellationToken cancellationToken)
    {
        var result = new List<Entry>();
        PhysicalMediaFileScannerDataAccessIoService.EnsureNoReparsePointsInPath(path);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(path));
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entryPath);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == FileAttributes.ReparsePoint) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!StringComparer.Ordinal.Equals(Path.GetFileName(entryPath), QuarantineDirectoryName)) pending.Push(entryPath);
                    continue;
                }
                var extension = Path.GetExtension(entryPath);
                if (!new[] { ".jpg", ".jpeg", ".png", ".heic", ".tif", ".tiff", ".dng", ".nef", ".cr2", ".arw", ".raf", ".mp4", ".mov" }.Contains(extension, StringComparer.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(entryPath);
                var relative = Path.GetRelativePath(path, entryPath).Replace('\\', '/');
                var model = new DiscoveredMediaFileDataAccessIoModel(Path.GetFullPath(entryPath), relative, info.Length, info.LastWriteTimeUtc);
                result.Add(new Entry(rootId, path, model, isDestination));
            }
        }
        return result;
    }

    private async Task PersistHashAsync(Entry entry, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={journalPath}");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ConsolidationHashes (RootId TEXT NOT NULL, RelativePath TEXT NOT NULL, Length INTEGER NOT NULL, LastWriteUtc TEXT NOT NULL, Algorithm TEXT NOT NULL, Digest TEXT NOT NULL, PRIMARY KEY (RootId, RelativePath)); INSERT INTO ConsolidationHashes (RootId, RelativePath, Length, LastWriteUtc, Algorithm, Digest) VALUES ($root, $path, $length, $write, 'BLAKE3', $digest) ON CONFLICT(RootId, RelativePath) DO UPDATE SET Length=excluded.Length, LastWriteUtc=excluded.LastWriteUtc, Algorithm=excluded.Algorithm, Digest=excluded.Digest;";
        command.Parameters.AddWithValue("$root", entry.RootId);
        command.Parameters.AddWithValue("$path", entry.Model.RelativePath);
        command.Parameters.AddWithValue("$length", entry.Model.Length);
        command.Parameters.AddWithValue("$write", entry.Model.LastWriteUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$digest", entry.Digest!);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string> PlanDestinationPathAsync(Entry entry, OrganizerProfileRemoteContractsModel profile, string? digest, CancellationToken cancellationToken)
    {
        var mappedProfile = new OrganizerProfileBusinessLogicModel(
            profile.FolderSegments.Select(segment => Enum.Parse<OrganizerFolderSegmentBusinessLogicModel>(segment.ToString(), ignoreCase: false)).ToArray(),
            profile.MissingDateFolder,
            profile.UnknownCameraFolder,
            profile.FileNameTemplate,
            Enum.Parse<OrganizerCollisionPolicyBusinessLogicModel>(profile.CollisionPolicy.ToString(), ignoreCase: false));
        var captureDate = await _metadataReader.ReadCaptureDateAsync(entry.Model.FullPath, cancellationToken);
        return _pathPlanner.Plan(mappedProfile, Path.GetFileName(entry.Model.FullPath), camera: null, captureDate, digest).RelativePath;
    }

    private async Task PersistRunAsync(OperationRequestRemoteContractsModel request, ConsolidationResultRemoteContractsModel result, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={journalPath}");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ConsolidationRuns (IdempotencyKey TEXT NOT NULL PRIMARY KEY, DestinationRootId TEXT NOT NULL, SourceRootIdsJson TEXT NOT NULL, Phase TEXT NOT NULL, ResultJson TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL); INSERT INTO ConsolidationRuns (IdempotencyKey, DestinationRootId, SourceRootIdsJson, Phase, ResultJson, CreatedAtUtc) VALUES ($key,$destination,$sources,$phase,$result,$created) ON CONFLICT(IdempotencyKey) DO UPDATE SET ResultJson=excluded.ResultJson;";
        command.Parameters.AddWithValue("$key", request.IdempotencyKey);
        command.Parameters.AddWithValue("$destination", request.RootId);
        command.Parameters.AddWithValue("$sources", System.Text.Json.JsonSerializer.Serialize(request.SourceRootIds));
        command.Parameters.AddWithValue("$phase", result.Phase);
        command.Parameters.AddWithValue("$result", System.Text.Json.JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ConsolidationResultRemoteContractsModel> LoadApprovedPlanAsync(
        OperationRequestRemoteContractsModel request,
        string phase,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={journalPath}");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DestinationRootId, SourceRootIdsJson, Phase, ResultJson FROM ConsolidationRuns WHERE IdempotencyKey=$key;";
        command.Parameters.AddWithValue("$key", request.PlanId!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("The referenced consolidation plan was not found.");
        if (!StringComparer.Ordinal.Equals(reader.GetString(0), request.RootId) ||
            !StringComparer.Ordinal.Equals(reader.GetString(1), System.Text.Json.JsonSerializer.Serialize(request.SourceRootIds)) ||
            !StringComparer.Ordinal.Equals(reader.GetString(2), "Preview"))
            throw new InvalidDataException("The referenced plan does not match this destination/source selection or is not a preview.");
        var plan = System.Text.Json.JsonSerializer.Deserialize<ConsolidationResultRemoteContractsModel>(reader.GetString(3), JsonOptions)
            ?? throw new InvalidDataException("The saved consolidation plan is invalid.");
        if (!StringComparer.Ordinal.Equals(System.Text.Json.JsonSerializer.Serialize(plan.OrganizerProfile, JsonOptions),
                System.Text.Json.JsonSerializer.Serialize(request.OrganizerProfile, JsonOptions)))
            throw new InvalidDataException("The selected ordering profile differs from the immutable preview.");
        var byId = plan.Items.ToDictionary(item => item.PlanItemId, StringComparer.Ordinal);
        foreach (var selectedId in request.SelectedPlanItemIds!)
        {
            if (!byId.TryGetValue(selectedId, out var item))
                throw new InvalidDataException("A selected plan item does not belong to this preview.");
            if (phase == "copy" && item.Action != "CopyRequired")
                throw new InvalidDataException("Only CopyRequired plan items can be copied.");
            if (phase == "quarantine" && item.Action is not ("AlreadyInDestination" or "CopyRequired" or "DuplicateSource"))
                throw new InvalidDataException("A selected plan item is not eligible for reversible quarantine.");
        }
        if (request.SelectedPlanItemIds!.Count != request.SelectedPlanItemIds.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("Selected plan item ids must be unique.");
        return plan;
    }

    private static ConsolidationPlanItemRemoteContractsModel Item(Entry entry, string action, string? target, string? reason = null) =>
        new(GetPlanItemId(entry.RootId, entry.Model.RelativePath), entry.RootId, entry.Model.RelativePath, entry.Model.Length,
            entry.Model.LastWriteUtc.ToUniversalTime(), "BLAKE3", Blake3ContentHasherBusinessLogicService.FullHashVersion,
            entry.Digest ?? string.Empty, action, target, reason);

    private static string GetPlanItemId(string rootId, string relativePath) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rootId + "\0" + relativePath))).ToLowerInvariant();

    private static bool IsContained(string root, string target)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(target).StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void CreateDirectoriesSafely(string root, string targetDirectory)
    {
        var canonicalRoot = Path.GetFullPath(root);
        var canonicalTarget = Path.GetFullPath(targetDirectory);
        if (!canonicalTarget.Equals(canonicalRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            !IsContained(canonicalRoot, canonicalTarget))
            throw new InvalidDataException("Computed directory escaped its configured root.");
        var relative = Path.GetRelativePath(canonicalRoot, canonicalTarget);
        var current = canonicalRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Consolidation refuses to traverse a symbolic link or reparse point.");
                continue;
            }
            Directory.CreateDirectory(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Consolidation refuses to traverse a symbolic link or reparse point.");
        }
    }

    private sealed class Entry(string rootId, string rootPath, DiscoveredMediaFileDataAccessIoModel model, bool isDestination)
    {
        public string RootId { get; } = rootId;
        public string RootPath { get; } = rootPath;
        public DiscoveredMediaFileDataAccessIoModel Model { get; } = model;
        public bool IsDestination { get; } = isDestination;
        public string? Digest { get; set; }
    }
}
