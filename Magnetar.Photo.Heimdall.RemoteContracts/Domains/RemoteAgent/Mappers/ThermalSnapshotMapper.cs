using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Mappers;

internal static class ThermalSnapshotMapper
{
    public static ThermalSnapshot Unavailable(string reason, DateTimeOffset observedAt) =>
        new(TelemetryAvailability.Unavailable, observedAt, [], reason);
}
