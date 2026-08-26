namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// A typed, idempotent work request.
/// IdempotencyKey is a client-generated opaque string (up to 128 chars)
/// that the agent uses to detect duplicates and return the original receipt.
/// RelativePath must be relative, forward-slash-separated, and free
/// of absolute markers, backslashes, null bytes, empty segments, '.' and '..'.
/// The agent resolves it exclusively under the root identified by RootId.
/// </summary>
public sealed record OperationRequest(
    ProtocolVersion Protocol,
    string RootId,
    string RelativePath,
    OperationKind Kind,
    int RequestedConcurrency,
    string IdempotencyKey)
{
    /// <summary>
    /// Validates the request against the agent's published capabilities.
    /// Throws RemoteContractValidationException on any violation.
    /// </summary>
    public void ValidateAgainst(AgentCapabilities capabilities)
    {
        Protocol.EnsureSupportedBy(capabilities.Protocol);

        if (!capabilities.AllowedRoots.Any(r => r.Id == RootId))
            throw new RemoteContractValidationException(
                $"Root '{RootId}' is not published by this agent.");

        RemotePath.ValidateRelative(RelativePath);

        if (!capabilities.AllowedOperations.Contains(Kind))
            throw new RemoteContractValidationException(
                $"Operation kind '{Kind}' is not allowed by this agent.");

        if (RequestedConcurrency is < 1 || RequestedConcurrency > capabilities.MaximumConcurrency)
            throw new RemoteContractValidationException(
                $"Requested concurrency {RequestedConcurrency} is outside the allowed range [1, {capabilities.MaximumConcurrency}].");

        if (string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > 128)
            throw new RemoteContractValidationException(
                "IdempotencyKey must be non-empty and no longer than 128 characters.");
    }
}