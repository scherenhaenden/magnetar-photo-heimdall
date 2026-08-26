using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Mappers;

internal static class ThermalSnapshotRemoteContractsMapper
{
    public static ThermalSnapshotRemoteContractsModel Unavailable(string reason, DateTimeOffset observedAt) =>
        new(TelemetryAvailabilityRemoteContractsModel.Unavailable, observedAt, [], reason);
}
