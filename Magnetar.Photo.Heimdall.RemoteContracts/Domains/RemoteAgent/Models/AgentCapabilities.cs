namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Describes what the remote agent can accept before any workload is submitted.
/// Obtained via <see cref="IRemoteAgentRpcV1.GetCapabilitiesAsync"/>.
/// </summary>
public sealed record AgentCapabilities(
    ProtocolVersion Protocol,
    IReadOnlyList<AgentRoot> AllowedRoots,
    IReadOnlyList<ThermalSensorCapability> ThermalSensors,
    IReadOnlySet<OperationKind> AllowedOperations,
    int MaximumConcurrency,
    bool ReceiptsSupported);