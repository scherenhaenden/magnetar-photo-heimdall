namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Services;

/// <summary>
/// Internal state of the thermal controller.
/// Cooling is a transient state between Critical and Normal
/// that is not part of the public ThermalState contract enum;
/// it is projected to AcceptNewWork=false, EffectiveConcurrency=0 in WorkloadDecision
/// and reported as ThermalState.Critical from the controller's CurrentState property
/// until hysteresis is satisfied.
/// </summary>
internal enum ThermalControllerState
{
    Normal,
    Warning,
    High,
    Critical,
    Cooling,
    Unavailable
}