namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// One sensor sample.  Confidence in [0,1].  ObservedAt is used by the
/// controller to detect stale readings and downgrade them to Unavailable.
/// </summary>
public sealed record ThermalReading(
    string SensorId,
    decimal Celsius,
    DateTimeOffset ObservedAt,
    decimal Confidence,
    ThermalState State);