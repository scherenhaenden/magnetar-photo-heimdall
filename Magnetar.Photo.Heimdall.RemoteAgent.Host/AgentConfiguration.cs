using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Host;

public sealed record AgentConfiguration(string JournalPath, int MaximumConcurrency, IReadOnlyList<AgentRootConfiguration> Roots, string? ThermalSensorDirectory = null, string? CatalogPath = null, WorkloadPolicyRemoteContractsModel? WorkloadPolicy = null);
public sealed record AgentRootConfiguration(string Id, string DisplayName, string Path);
