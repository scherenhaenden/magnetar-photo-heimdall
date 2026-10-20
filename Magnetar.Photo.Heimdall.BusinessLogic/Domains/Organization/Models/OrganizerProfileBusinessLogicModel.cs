namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Models;

/// <summary>Per-library rules for deriving relative destination paths from metadata.</summary>
public sealed record OrganizerProfileBusinessLogicModel(
    IReadOnlyList<OrganizerFolderSegmentBusinessLogicModel> FolderSegments,
    string MissingDateFolder,
    string UnknownCameraFolder,
    string FileNameTemplate,
    OrganizerCollisionPolicyBusinessLogicModel CollisionPolicy);
