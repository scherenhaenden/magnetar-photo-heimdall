namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Versioned RPC contract between Heimdall client and a remote SSH agent.
/// All methods are typed. No method accepts or executes arbitrary shell text.
/// </summary>
public interface IRemoteAgentRpcV1RemoteContractsContract
{
    /// <summary>Discover what the agent supports before submitting work.</summary>
    Task<AgentCapabilitiesRemoteContractsModel> GetCapabilitiesAsync(ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default);

    /// <summary>Check agent liveness and health status.</summary>
    Task<AgentHealthSnapshotRemoteContractsModel> GetHealthAsync(ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve thermal readings.  Platforms without sensors return Unavailable,
    /// never a fabricated temperature.
    /// </summary>
    Task<ThermalSnapshotRemoteContractsModel> GetThermalAsync(IReadOnlyList<string> sensorIds, CancellationToken cancellationToken = default);

    /// <summary>Submit a typed, idempotent work unit.</summary>
    Task<OperationReceiptRemoteContractsModel> SubmitOperationAsync(OperationRequestRemoteContractsModel request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a previously issued receipt — essential for recovery after disconnect.
    /// </summary>
    Task<OperationReceiptRemoteContractsModel> GetReceiptAsync(string receiptId, ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default);
}