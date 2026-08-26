namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Aggregate thermal snapshot.  If the platform has no sensor, or permission
/// was denied, Availability is Unavailable and Readings is empty.
/// No fabricated temperatures are ever returned.
/// </summary>
public sealed record ThermalSnapshotRemoteContractsModel(
    TelemetryAvailabilityRemoteContractsModel Availability,
    DateTimeOffset ObservedAt,
    IReadOnlyList<ThermalReadingRemoteContractsModel> Readings,
    string? Reason = null);