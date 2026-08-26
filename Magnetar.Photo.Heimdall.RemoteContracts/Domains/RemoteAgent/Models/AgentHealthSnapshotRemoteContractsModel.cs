namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>Point-in-time health snapshot from the remote agent.</summary>
public sealed record AgentHealthSnapshotRemoteContractsModel(
    AgentHealthStatusRemoteContractsModel Status,
    DateTimeOffset ObservedAt,
    string? Detail = null);