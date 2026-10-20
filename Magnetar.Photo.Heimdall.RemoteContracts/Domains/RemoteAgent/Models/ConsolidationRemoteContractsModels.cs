namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

public enum OrganizerFolderSegmentRemoteContractsModel { Camera, Year, Month }
public enum OrganizerCollisionPolicyRemoteContractsModel { Fail }

/// <summary>Ordering rules selected for one target library and carried with every consolidation receipt.</summary>
public sealed record OrganizerProfileRemoteContractsModel(
    IReadOnlyList<OrganizerFolderSegmentRemoteContractsModel> FolderSegments,
    string MissingDateFolder,
    string UnknownCameraFolder,
    string FileNameTemplate,
    OrganizerCollisionPolicyRemoteContractsModel CollisionPolicy)
{
    public void Validate()
    {
        if (FolderSegments is null || FolderSegments.Distinct().Count() != FolderSegments.Count)
            throw new RemoteContractsValidationException("Organizer folder segments must be present and unique.");
        if (string.IsNullOrWhiteSpace(MissingDateFolder) || string.IsNullOrWhiteSpace(UnknownCameraFolder) ||
            !SafeFolder(MissingDateFolder) || !SafeFolder(UnknownCameraFolder))
            throw new RemoteContractsValidationException("Organizer fallback folders must be safe single path segments.");
        if (string.IsNullOrWhiteSpace(FileNameTemplate) || FileNameTemplate.Length > 160 || FileNameTemplate.Contains('/') || FileNameTemplate.Contains('\\') ||
            FileNameTemplate.Contains("..", StringComparison.Ordinal))
            throw new RemoteContractsValidationException("Organizer filename template must be a safe filename template.");
        if (CollisionPolicy != OrganizerCollisionPolicyRemoteContractsModel.Fail)
            throw new RemoteContractsValidationException("Only fail-closed filename collision handling is supported.");
    }

    private static bool SafeFolder(string value) => value is not "." and not ".." &&
        !value.Contains('/') && !value.Contains('\\') && !value.Contains(':') && !value.Any(char.IsControl);
}

/// <summary>A file-level decision from a remote consolidation preview.</summary>
public sealed record ConsolidationPlanItemRemoteContractsModel(
    string PlanItemId,
    string SourceRootId,
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteUtc,
    string HashAlgorithm,
    int HashVersion,
    string Blake3,
    string Action,
    string? DestinationRelativePath = null,
    string? Reason = null);

/// <summary>Result of a NAS-side consolidation plan or execution.</summary>
public sealed record ConsolidationResultRemoteContractsModel(
    string PlanId,
    string DestinationRootId,
    IReadOnlyList<string> SourceRootIds,
    string Phase,
    int FilesExamined,
    int HashesComputed,
    int FilesCopied,
    int FilesAlreadyPresent,
    int FilesQuarantined,
    IReadOnlyList<ConsolidationPlanItemRemoteContractsModel> Items,
    IReadOnlyList<string> Issues,
    OrganizerProfileRemoteContractsModel? OrganizerProfile = null);
