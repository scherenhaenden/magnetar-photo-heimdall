namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// A named, opaque root that the agent exposes.
/// The agent resolves <c>OperationRequestRemoteContractsModel.RelativePath</c> only under this root,
/// never outside it.
/// </summary>
public sealed record AgentRootRemoteContractsModel(string Id, string DisplayName)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64
                                          || !Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new RemoteContractsValidationException(
                "Root id must contain only ASCII letters, digits, '-', or '_', and be no longer than 64 characters.");
        }
    }
}