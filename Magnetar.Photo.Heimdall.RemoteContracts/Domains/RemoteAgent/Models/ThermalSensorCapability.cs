namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>Describes what a sensor can report. All nullable fields signal unknown limits.</summary>
public sealed record ThermalSensorCapability(
    string SensorId,
    string Kind,
    decimal? MinimumCelsius,
    decimal? MaximumCelsius,
    decimal? PrecisionCelsius);