namespace Magnetar.Photo.Heimdall.RemoteContracts;

// ---------------------------------------------------------------------------
// Internal controller state (superset of public ThermalState contract enum)
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Thermal state machine — pure, deterministic, no I/O
// ---------------------------------------------------------------------------

/// <summary>
/// Per-host thermal state machine.
///
/// Rules enforced:
///   - Never executes remote shell commands.
///   - Readings older than WorkloadPolicy.ReadingStalenessWindow are treated as Unavailable.
///   - In Critical: EffectiveConcurrency = 0, AcceptNewWork = false.
///   - In Cooling: stays until both conditions hold:
///       (a) temperature less than WarningCelsius - HysteresisCelsius, AND
///       (b) at least MinimumCoolingDuration has elapsed since entering Critical.
///   - Unavailable: concurrency is capped at HighConcurrency as a safe default;
///       new work is still accepted (system is assumed safe, not hot).
///   - The controller is not thread-safe; synchronise externally if needed.
/// </summary>
public sealed class ThermalWorkloadController
{
    private readonly WorkloadPolicy _policy;
    private DateTimeOffset? _criticalSince;
    private ThermalControllerState _state = ThermalControllerState.Unavailable;

    public ThermalWorkloadController(WorkloadPolicy policy)
    {
        policy.Validate();
        _policy = policy;
    }

    /// <summary>
    /// Current public state, exposed for diagnostics.
    /// Cooling maps to ThermalState.Critical externally (work is still blocked).
    /// </summary>
    public ThermalState CurrentState => _state switch
    {
        ThermalControllerState.Normal => ThermalState.Normal,
        ThermalControllerState.Warning => ThermalState.Warning,
        ThermalControllerState.High => ThermalState.High,
        ThermalControllerState.Critical => ThermalState.Critical,
        ThermalControllerState.Cooling => ThermalState.Critical,
        _ => ThermalState.Unavailable
    };

    /// <summary>
    /// Processes one thermal snapshot and returns the workload decision to apply.
    /// now is injected to keep this method deterministic and unit-testable.
    /// </summary>
    public WorkloadDecision Observe(ThermalSnapshot snapshot, DateTimeOffset now)
    {
        // Unavailable: no sensor, permission denied, or all readings are stale.
        if (snapshot.Availability == TelemetryAvailability.Unavailable
            || snapshot.Readings.Count == 0
            || AllReadingsStale(snapshot.Readings, now))
        {
            _state = ThermalControllerState.Unavailable;
            var unavailReason = snapshot.Reason
                ?? (snapshot.Readings.Count > 0
                    ? "All readings exceeded the staleness window."
                    : "Thermal telemetry is unavailable.");
            // Conservative limit — not zero, because we cannot confirm heat.
            return Decision(ThermalState.Unavailable, _policy.HighConcurrency, true, unavailReason);
        }

        var peak = snapshot.Readings.Max(r => r.Celsius);

        // ---- Critical entry ----
        if (peak >= _policy.CriticalCelsius)
        {
            _criticalSince ??= now;
            _state = ThermalControllerState.Critical;
            return Decision(ThermalState.Critical, 0, false,
                $"Critical thermal threshold ({_policy.CriticalCelsius} C) reached: peak {peak} C.");
        }

        // ---- Cooling hysteresis ----
        if (_state is ThermalControllerState.Critical or ThermalControllerState.Cooling)
        {
            var coolTarget = _policy.WarningCelsius - _policy.HysteresisCelsius;
            var elapsed = now - _criticalSince!.Value;

            if (peak >= coolTarget || elapsed < _policy.MinimumCoolingDuration)
            {
                _state = ThermalControllerState.Cooling;
                return Decision(ThermalState.Critical, 0, false,
                    $"Cooling: peak {peak} C (target < {coolTarget} C), elapsed {elapsed.TotalSeconds:F0}s of {_policy.MinimumCoolingDuration.TotalSeconds:F0}s minimum.");
            }

            // Hysteresis satisfied — reset and fall through to normal classification.
            _criticalSince = null;
        }

        // ---- Normal classification ----
        _state = peak >= _policy.HighCelsius ? ThermalControllerState.High
               : peak >= _policy.WarningCelsius ? ThermalControllerState.Warning
               : ThermalControllerState.Normal;

        return _state switch
        {
            ThermalControllerState.High =>
                Decision(ThermalState.High, _policy.HighConcurrency, false,
                    $"High thermal threshold ({_policy.HighCelsius} C) reached: peak {peak} C. New heavy work paused."),
            ThermalControllerState.Warning =>
                Decision(ThermalState.Warning, _policy.WarningConcurrency, true,
                    $"Warning thermal threshold ({_policy.WarningCelsius} C) reached: peak {peak} C. Concurrency reduced."),
            _ =>
                Decision(ThermalState.Normal, _policy.NormalConcurrency, true,
                    $"Temperature nominal: peak {peak} C."),
        };
    }

    private bool AllReadingsStale(IReadOnlyList<ThermalReading> readings, DateTimeOffset now)
        => readings.All(r => now - r.ObservedAt > _policy.ReadingStalenessWindow);

    private static WorkloadDecision Decision(
        ThermalState state, int concurrency, bool accept, string reason)
        => new(state, concurrency, accept, reason);
}

// ---------------------------------------------------------------------------
// Platform-Unavailable thermal provider
// ---------------------------------------------------------------------------

/// <summary>
/// Thermal provider for platforms that have no accessible sensor (e.g. macOS
/// without entitlement, sandboxed environments, CI runners).
///
/// Returns a genuine Unavailable snapshot — never fabricates Celsius readings.
/// Clients and the ThermalWorkloadController must treat this as "safe but unknown"
/// and apply conservative concurrency (HighConcurrency in the default policy).
/// </summary>
public sealed class UnavailableThermalProvider
{
    private readonly string _reason;

    public UnavailableThermalProvider(string reason = "No thermal sensor is accessible on this platform.")
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason must not be empty.", nameof(reason));
        _reason = reason;
    }

    /// <summary>Always returns an Unavailable snapshot with an empty reading list.</summary>
    public ThermalSnapshot GetSnapshot() =>
        new(TelemetryAvailability.Unavailable, DateTimeOffset.UtcNow, [], _reason);
}
