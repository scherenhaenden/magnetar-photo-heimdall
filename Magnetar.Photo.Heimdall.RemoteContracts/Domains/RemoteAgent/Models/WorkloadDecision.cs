namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Output of the thermal controller: what concurrency to use and whether to accept new work.
/// </summary>
public sealed record WorkloadDecision(
    ThermalState State,
    int EffectiveConcurrency,
    bool AcceptNewWork,
    string Reason);