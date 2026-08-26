namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Describes what the remote agent can accept before any workload is submitted.
/// Obtained via <see cref="IRemoteAgentRpcV1RemoteContractsContract.GetCapabilitiesAsync"/>.
/// </summary>
public sealed record AgentCapabilitiesRemoteContractsModel(
    ProtocolVersionRemoteContractsModel Protocol,
    IReadOnlyList<AgentRootRemoteContractsModel> AllowedRoots,
    IReadOnlyList<ThermalSensorCapabilityRemoteContractsModel> ThermalSensors,
    IReadOnlySet<OperationKindRemoteContractsModel> AllowedOperations,
    int MaximumConcurrency,
    bool ReceiptsSupported);