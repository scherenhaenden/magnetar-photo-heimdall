namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Services;

/// <summary>
/// Internal state of the thermal controller.
/// Cooling is a transient state between Critical and Normal
/// that is not part of the public ThermalStateRemoteContractsModel contract enum;
/// it is projected to AcceptNewWork=false, EffectiveConcurrency=0 in WorkloadDecisionRemoteContractsModel
/// and reported as ThermalStateRemoteContractsModel.Critical from the controller's CurrentState property
/// until hysteresis is satisfied.
/// </summary>
internal enum ThermalControllerRemoteContractsServiceState
{
    Normal,
    Warning,
    High,
    Critical,
    Cooling,
    Unavailable
}