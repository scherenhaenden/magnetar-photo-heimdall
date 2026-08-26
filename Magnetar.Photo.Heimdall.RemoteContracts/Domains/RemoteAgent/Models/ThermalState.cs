namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Classified thermal state of a single reading or aggregate channel.
/// <c>Unavailable</c> means no reliable reading — never a fabricated Celsius value.
/// </summary>
public enum ThermalState
{
    Normal,
    Warning,
    High,
    Critical,
    Unavailable
}