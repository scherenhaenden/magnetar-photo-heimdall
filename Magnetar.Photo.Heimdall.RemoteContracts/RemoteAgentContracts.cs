namespace Magnetar.Photo.Heimdall.RemoteContracts;

/// <summary>Versioned, typed RPC surface carried over an administrator-configured SSH tunnel.</summary>
public interface IRemoteAgentRpcV1
{
    Task<AgentCapabilities> GetCapabilitiesAsync(ProtocolVersion version, CancellationToken cancellationToken = default);
    Task<AgentHealth> GetHealthAsync(ProtocolVersion version, CancellationToken cancellationToken = default);
    Task<ThermalSnapshot> GetThermalAsync(ThermalSnapshotRequest request, CancellationToken cancellationToken = default);
    Task<WorkloadReceipt> SubmitWorkloadAsync(WorkloadRequest request, CancellationToken cancellationToken = default);
    Task<WorkloadReceipt> GetReceiptAsync(ReceiptRequest request, CancellationToken cancellationToken = default);
}

public readonly record struct ProtocolVersion(int Major, int Minor)
{
    public static ProtocolVersion V1 { get; } = new(1, 0);

    public void EnsureSupportedBy(ProtocolVersion agentVersion)
    {
        if (Major != agentVersion.Major || Minor > agentVersion.Minor)
        {
            throw new RemoteContractValidationException($"Unsupported protocol {Major}.{Minor}; agent supports {agentVersion.Major}.{agentVersion.Minor}.");
        }
    }
}

public sealed record AgentCapabilities(
    ProtocolVersion Protocol,
    IReadOnlyList<RemoteRoot> AllowedRoots,
    IReadOnlyList<ThermalSensorCapability> ThermalSensors,
    IReadOnlySet<WorkloadKind> AllowedWorkloads,
    int MaximumConcurrency,
    bool ReceiptsSupported);

public sealed record RemoteRoot(string Id, string DisplayName)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || !Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new RemoteContractValidationException("Root id must contain only ASCII letters, digits, '-' or '_'.");
        }
    }
}

public sealed record AgentHealth(AgentHealthStatus Status, DateTimeOffset ObservedAt, string? Detail = null);
public enum AgentHealthStatus { Healthy, Degraded, Unavailable }

public sealed record ThermalSensorCapability(string SensorId, string Kind, decimal? MinimumCelsius, decimal? MaximumCelsius, decimal? PrecisionCelsius);
public sealed record ThermalSnapshotRequest(ProtocolVersion Protocol, IReadOnlyList<string> SensorIds);
public sealed record ThermalSnapshot(TelemetryAvailability Availability, DateTimeOffset ObservedAt, IReadOnlyList<ThermalReading> Readings, string? Reason = null);
public enum TelemetryAvailability { Available, Unavailable }
public sealed record ThermalReading(string SensorId, decimal Celsius, DateTimeOffset ObservedAt, decimal Confidence, string ThermalState);

public sealed record WorkloadRequest(ProtocolVersion Protocol, string RootId, string RelativePath, WorkloadKind Kind, int RequestedConcurrency, string IdempotencyKey)
{
    public void ValidateAgainst(AgentCapabilities capabilities)
    {
        Protocol.EnsureSupportedBy(capabilities.Protocol);
        if (!capabilities.AllowedRoots.Any(root => root.Id == RootId)) throw new RemoteContractValidationException("The root is not allowed by this agent.");
        RemotePath.ValidateRelative(RelativePath);
        if (!capabilities.AllowedWorkloads.Contains(Kind)) throw new RemoteContractValidationException("The workload kind is not allowed by this agent.");
        if (RequestedConcurrency is < 1 || RequestedConcurrency > capabilities.MaximumConcurrency) throw new RemoteContractValidationException("Requested concurrency exceeds the agent policy.");
        if (string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > 128) throw new RemoteContractValidationException("An idempotency key is required.");
    }
}

public enum WorkloadKind { Scan, Hash, Preview, Export, Transfer }
public sealed record ReceiptRequest(ProtocolVersion Protocol, string ReceiptId);
public sealed record WorkloadReceipt(string ReceiptId, WorkloadStatus Status, DateTimeOffset AcceptedAt, int EffectiveConcurrency, string? Reason = null);
public enum WorkloadStatus { Accepted, Running, PausedThermal, Completed, Rejected }

public static class RemotePath
{
    public static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains('\0'))
        {
            throw new RemoteContractValidationException("A non-empty, slash-separated relative path is required.");
        }

        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new RemoteContractValidationException("Path traversal and empty segments are not allowed.");
        }
    }
}

public sealed class RemoteContractValidationException(string message) : ArgumentException(message);
