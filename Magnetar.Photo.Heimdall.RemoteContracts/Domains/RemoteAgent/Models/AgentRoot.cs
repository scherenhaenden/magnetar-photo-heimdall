namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// A named, opaque root that the agent exposes.
/// The agent resolves <c>OperationRequest.RelativePath</c> only under this root,
/// never outside it.
/// </summary>
public sealed record AgentRoot(string Id, string DisplayName)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64
                                          || !Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new RemoteContractValidationException(
                "Root id must contain only ASCII letters, digits, '-', or '_', and be no longer than 64 characters.");
        }
    }
}