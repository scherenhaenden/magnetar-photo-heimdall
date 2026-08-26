using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Mappers;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Services;

/// <summary>
/// Thermal provider for platforms that have no accessible sensor (e.g. macOS
/// without entitlement, sandboxed environments, CI runners).
///
/// Returns a genuine Unavailable snapshot — never fabricates Celsius readings.
/// Clients and the ThermalWorkloadRemoteContractsService must treat this as "safe but unknown"
/// and apply conservative concurrency (HighConcurrency in the default policy).
/// </summary>
public sealed class UnavailableThermalRemoteContractsService
{
    private readonly string _reason;

    public UnavailableThermalRemoteContractsService(string reason = "No thermal sensor is accessible on this platform.")
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason must not be empty.", nameof(reason));
        _reason = reason;
    }

    /// <summary>Always returns an Unavailable snapshot with an empty reading list.</summary>
    public ThermalSnapshotRemoteContractsModel GetSnapshot() =>
        ThermalSnapshotRemoteContractsMapper.Unavailable(_reason, DateTimeOffset.UtcNow);
}