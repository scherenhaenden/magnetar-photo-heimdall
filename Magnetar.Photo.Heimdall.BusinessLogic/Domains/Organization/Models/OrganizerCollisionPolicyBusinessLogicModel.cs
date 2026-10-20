namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Models;

/// <summary>Behavior required when the planned destination already exists.</summary>
public enum OrganizerCollisionPolicyBusinessLogicModel
{
    /// <summary>Fail without replacing the existing destination.</summary>
    Fail = 0,
}
