namespace Magnetar.Photo.Heimdall.RemoteContracts;

// ---------------------------------------------------------------------------
// Protocol versioning
// ---------------------------------------------------------------------------

/// <summary>
/// Identifies the RPC wire protocol negotiated between client and remote agent.
/// Breaking changes require a Major bump; backward-compatible extensions bump Minor.
/// </summary>
public readonly record struct ProtocolVersion(int Major, int Minor)
{
    public static ProtocolVersion V1 { get; } = new(1, 0);

    /// <summary>
    /// Ensures the caller's requested version is served by <paramref name="agentVersion"/>.
    /// Same Major required; caller's Minor must be ≤ agent's Minor.
    /// </summary>
    public void EnsureSupportedBy(ProtocolVersion agentVersion)
    {
        if (Major != agentVersion.Major || Minor > agentVersion.Minor)
        {
            throw new RemoteContractValidationException(
                $"Unsupported protocol {Major}.{Minor}; agent supports {agentVersion.Major}.{agentVersion.Minor}.");
        }
    }
}

// ---------------------------------------------------------------------------
// Capabilities
// ---------------------------------------------------------------------------

/// <summary>
/// Describes what the remote agent can accept before any workload is submitted.
/// Obtained via <see cref="IRemoteAgentRpcV1.GetCapabilitiesAsync"/>.
/// </summary>
public sealed record AgentCapabilities(
    ProtocolVersion Protocol,
    IReadOnlyList<AgentRoot> AllowedRoots,
    IReadOnlyList<ThermalSensorCapability> ThermalSensors,
    IReadOnlySet<OperationKind> AllowedOperations,
    int MaximumConcurrency,
    bool ReceiptsSupported);

/// <summary>
/// A named, opaque root that the agent exposes.
/// The agent resolves <c>OperationRequest.RelativePath</c> only under this root,
/// never outside it.
/// </summary>
public sealed record AgentRoot(string Id, string DisplayName)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64
            || !Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new RemoteContractValidationException(
                "Root id must contain only ASCII letters, digits, '-', or '_', and be no longer than 64 characters.");
        }
    }
}

// ---------------------------------------------------------------------------
// Health
// ---------------------------------------------------------------------------

/// <summary>Point-in-time health snapshot from the remote agent.</summary>
public sealed record AgentHealthSnapshot(
    AgentHealthStatus Status,
    DateTimeOffset ObservedAt,
    string? Detail = null);

public enum AgentHealthStatus { Healthy, Degraded, Unavailable }

// ---------------------------------------------------------------------------
// Thermal telemetry
// ---------------------------------------------------------------------------

/// <summary>Describes what a sensor can report. All nullable fields signal unknown limits.</summary>
public sealed record ThermalSensorCapability(
    string SensorId,
    string Kind,
    decimal? MinimumCelsius,
    decimal? MaximumCelsius,
    decimal? PrecisionCelsius);

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

/// <summary>
/// Aggregate thermal snapshot.  If the platform has no sensor, or permission
/// was denied, Availability is Unavailable and Readings is empty.
/// No fabricated temperatures are ever returned.
/// </summary>
public sealed record ThermalSnapshot(
    TelemetryAvailability Availability,
    DateTimeOffset ObservedAt,
    IReadOnlyList<ThermalReading> Readings,
    string? Reason = null);

public enum TelemetryAvailability { Available, Unavailable }

// ---------------------------------------------------------------------------
// Workload policy and decision
// ---------------------------------------------------------------------------

/// <summary>
/// Policy parameters that govern workload acceptance under thermal pressure.
/// All concurrency values must satisfy: HighConcurrency le WarningConcurrency le NormalConcurrency.
/// </summary>
public sealed record WorkloadPolicy(
    decimal WarningCelsius,
    decimal HighCelsius,
    decimal CriticalCelsius,
    decimal HysteresisCelsius,
    TimeSpan MinimumCoolingDuration,
    TimeSpan ReadingStalenessWindow,
    int NormalConcurrency,
    int WarningConcurrency,
    int HighConcurrency)
{
    public void Validate()
    {
        if (WarningCelsius >= HighCelsius
            || HighCelsius >= CriticalCelsius
            || HysteresisCelsius < 0
            || MinimumCoolingDuration < TimeSpan.Zero
            || ReadingStalenessWindow <= TimeSpan.Zero
            || NormalConcurrency < 1
            || WarningConcurrency < 1
            || HighConcurrency < 1
            || WarningConcurrency > NormalConcurrency
            || HighConcurrency > WarningConcurrency)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkloadPolicy),
                "Thermal thresholds and concurrency limits are inconsistent.");
        }
    }
}

/// <summary>
/// Output of the thermal controller: what concurrency to use and whether to accept new work.
/// </summary>
public sealed record WorkloadDecision(
    ThermalState State,
    int EffectiveConcurrency,
    bool AcceptNewWork,
    string Reason);

// ---------------------------------------------------------------------------
// Operations (typed RPC — never shell commands)
// ---------------------------------------------------------------------------

public enum OperationKind { Scan, Hash, Preview, Export, Transfer }

/// <summary>
/// A typed, idempotent work request.
/// IdempotencyKey is a client-generated opaque string (up to 128 chars)
/// that the agent uses to detect duplicates and return the original receipt.
/// RelativePath must be relative, forward-slash-separated, and free
/// of absolute markers, backslashes, null bytes, empty segments, '.' and '..'.
/// The agent resolves it exclusively under the root identified by RootId.
/// </summary>
public sealed record OperationRequest(
    ProtocolVersion Protocol,
    string RootId,
    string RelativePath,
    OperationKind Kind,
    int RequestedConcurrency,
    string IdempotencyKey)
{
    /// <summary>
    /// Validates the request against the agent's published capabilities.
    /// Throws RemoteContractValidationException on any violation.
    /// </summary>
    public void ValidateAgainst(AgentCapabilities capabilities)
    {
        Protocol.EnsureSupportedBy(capabilities.Protocol);

        if (!capabilities.AllowedRoots.Any(r => r.Id == RootId))
            throw new RemoteContractValidationException(
                $"Root '{RootId}' is not published by this agent.");

        RemotePath.ValidateRelative(RelativePath);

        if (!capabilities.AllowedOperations.Contains(Kind))
            throw new RemoteContractValidationException(
                $"Operation kind '{Kind}' is not allowed by this agent.");

        if (RequestedConcurrency is < 1 || RequestedConcurrency > capabilities.MaximumConcurrency)
            throw new RemoteContractValidationException(
                $"Requested concurrency {RequestedConcurrency} is outside the allowed range [1, {capabilities.MaximumConcurrency}].");

        if (string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > 128)
            throw new RemoteContractValidationException(
                "IdempotencyKey must be non-empty and no longer than 128 characters.");
    }
}

/// <summary>
/// Durable receipt returned for every submitted OperationRequest.
/// Re-submitting the same IdempotencyKey returns the original receipt.
/// </summary>
public sealed record OperationReceipt(
    string ReceiptId,
    string IdempotencyKey,
    OperationStatus Status,
    DateTimeOffset AcceptedAt,
    int EffectiveConcurrency,
    string? Reason = null);

public enum OperationStatus { Accepted, Running, PausedThermal, Completed, Rejected }

// ---------------------------------------------------------------------------
// RPC surface  (versioned, typed — never executes shell)
// ---------------------------------------------------------------------------

/// <summary>
/// Versioned RPC contract between Heimdall client and a remote SSH agent.
/// All methods are typed. No method accepts or executes arbitrary shell text.
/// </summary>
public interface IRemoteAgentRpcV1
{
    /// <summary>Discover what the agent supports before submitting work.</summary>
    Task<AgentCapabilities> GetCapabilitiesAsync(ProtocolVersion version, CancellationToken cancellationToken = default);

    /// <summary>Check agent liveness and health status.</summary>
    Task<AgentHealthSnapshot> GetHealthAsync(ProtocolVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve thermal readings.  Platforms without sensors return Unavailable,
    /// never a fabricated temperature.
    /// </summary>
    Task<ThermalSnapshot> GetThermalAsync(IReadOnlyList<string> sensorIds, CancellationToken cancellationToken = default);

    /// <summary>Submit a typed, idempotent work unit.</summary>
    Task<OperationReceipt> SubmitOperationAsync(OperationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a previously issued receipt — essential for recovery after disconnect.
    /// </summary>
    Task<OperationReceipt> GetReceiptAsync(string receiptId, ProtocolVersion version, CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------
// Path validation helpers
// ---------------------------------------------------------------------------

/// <summary>
/// Validates relative paths used in OperationRequest.
/// Absolute paths, backslashes, null bytes, empty segments, and traversal
/// sequences are all rejected at the contract layer before any I/O occurs.
/// </summary>
public static class RemotePath
{
    public static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096
            || Path.IsPathRooted(path)
            || path.Contains('\\')
            || path.Contains('\0'))
        {
            throw new RemoteContractValidationException(
                "A non-empty, slash-separated relative path without backslashes or null bytes is required.");
        }

        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Any(s => s is "" or "." or ".."))
        {
            throw new RemoteContractValidationException(
                "Path traversal sequences ('..'), empty segments, and '.' are not allowed.");
        }
    }
}

// ---------------------------------------------------------------------------
// Exceptions
// ---------------------------------------------------------------------------

public sealed class RemoteContractValidationException(string message) : ArgumentException(message);
