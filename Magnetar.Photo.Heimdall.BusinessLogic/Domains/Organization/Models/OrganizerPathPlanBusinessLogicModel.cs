namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Models;

/// <summary>A sanitized, destination-relative path and its no-overwrite policy.</summary>
public sealed record OrganizerPathPlanBusinessLogicModel(
    string RelativePath,
    OrganizerCollisionPolicyBusinessLogicModel CollisionPolicy);
