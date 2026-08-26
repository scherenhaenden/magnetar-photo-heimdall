namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Versioned RPC contract between Heimdall client and a remote SSH agent.
/// All methods are typed. No method accepts or executes arbitrary shell text.
/// </summary>
public interface IRemoteAgentRpcV1
{
    /// <summary>Discover what the agent supports before submitting work.</summary>
    Task<AgentCapabilities> GetCapabilitiesAsync(ProtocolVersion version, CancellationToken cancellationToken = default);

    /// <summary>Check agent liveness and health status.</summary>
    Task<AgentHealthSnapshot> GetHealthAsync(ProtocolVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve thermal readings.  Platforms without sensors return Unavailable,
    /// never a fabricated temperature.
    /// </summary>
    Task<ThermalSnapshot> GetThermalAsync(IReadOnlyList<string> sensorIds, CancellationToken cancellationToken = default);

    /// <summary>Submit a typed, idempotent work unit.</summary>
    Task<OperationReceipt> SubmitOperationAsync(OperationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a previously issued receipt — essential for recovery after disconnect.
    /// </summary>
    Task<OperationReceipt> GetReceiptAsync(string receiptId, ProtocolVersion version, CancellationToken cancellationToken = default);
}