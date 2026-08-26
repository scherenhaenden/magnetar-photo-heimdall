namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Output of the thermal controller: what concurrency to use and whether to accept new work.
/// </summary>
public sealed record WorkloadDecisionRemoteContractsModel(
    ThermalStateRemoteContractsModel State,
    int EffectiveConcurrency,
    bool AcceptNewWork,
    string Reason);