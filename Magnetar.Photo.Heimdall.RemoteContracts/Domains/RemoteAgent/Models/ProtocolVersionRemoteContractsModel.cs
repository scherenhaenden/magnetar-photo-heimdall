namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

// ---------------------------------------------------------------------------
// Protocol versioning
// ---------------------------------------------------------------------------

/// <summary>
/// Identifies the RPC wire protocol negotiated between client and remote agent.
/// Breaking changes require a Major bump; backward-compatible extensions bump Minor.
/// </summary>
public readonly record struct ProtocolVersionRemoteContractsModel(int Major, int Minor)
{
    public static ProtocolVersionRemoteContractsModel V1 { get; } = new(1, 0);

    /// <summary>
    /// Ensures the caller's requested version is served by <paramref name="agentVersion"/>.
    /// Same Major required; caller's Minor must be ≤ agent's Minor.
    /// </summary>
    public void EnsureSupportedBy(ProtocolVersionRemoteContractsModel agentVersion)
    {
        if (Major != agentVersion.Major || Minor > agentVersion.Minor)
        {
            throw new RemoteContractsValidationException(
                $"Unsupported protocol {Major}.{Minor}; agent supports {agentVersion.Major}.{agentVersion.Minor}.");
        }
    }
}
