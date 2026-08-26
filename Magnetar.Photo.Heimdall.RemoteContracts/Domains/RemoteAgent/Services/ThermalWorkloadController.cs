using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Services;

// ---------------------------------------------------------------------------
// Thermal state machine — pure, deterministic, no I/O
// ---------------------------------------------------------------------------

/// <summary>
/// Per-host thermal state machine.
///
/// Rules enforced:
///   - Never executes remote shell commands.
///   - Missing, stale, future-dated or physically invalid readings are treated as Unavailable.
///   - In Critical: EffectiveConcurrency = 0, AcceptNewWork = false.
///   - In Cooling: stays until both conditions hold:
///       (a) temperature less than WarningCelsius - HysteresisCelsius, AND
///       (b) that safe temperature remains continuous for MinimumCoolingDuration.
///   - Unavailable: blocks new work because heat cannot be trusted.
///   - The controller is not thread-safe; synchronise externally if needed.
/// </summary>
public sealed class ThermalWorkloadController
{
    private readonly WorkloadPolicy _policy;
    private DateTimeOffset? _criticalSince;
    private DateTimeOffset? _safeSince;
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
        if (!HasUsableTelemetry(snapshot, now))
        {
            if (_criticalSince is not null)
            {
                _safeSince = null;
                _state = ThermalControllerState.Cooling;
                return Decision(ThermalState.Critical, 0, false,
                    snapshot.Reason ?? "Thermal telemetry is unavailable while cooling is required.");
            }

            _state = ThermalControllerState.Unavailable;
            return Decision(ThermalState.Unavailable, 0, false,
                snapshot.Reason ?? "Thermal telemetry is unavailable, stale, future-dated, or invalid.");
        }

        var peak = snapshot.Readings.Max(r => r.Celsius);

        // ---- Critical entry ----
        if (peak >= _policy.CriticalCelsius)
        {
            _criticalSince ??= now;
            _safeSince = null;
            _state = ThermalControllerState.Critical;
            return Decision(ThermalState.Critical, 0, false,
                $"Critical thermal threshold ({_policy.CriticalCelsius} C) reached: peak {peak} C.");
        }

        // ---- Cooling hysteresis ----
        if (_criticalSince is not null)
        {
            var coolTarget = _policy.WarningCelsius - _policy.HysteresisCelsius;
            if (peak >= coolTarget)
            {
                _safeSince = null;
                _state = ThermalControllerState.Cooling;
                return Decision(ThermalState.Critical, 0, false,
                    $"Cooling: peak {peak} C must be below {coolTarget} C.");
            }

            _safeSince ??= now;
            var safeElapsed = now - _safeSince.Value;
            if (safeElapsed < _policy.MinimumCoolingDuration)
            {
                _state = ThermalControllerState.Cooling;
                return Decision(ThermalState.Critical, 0, false,
                    $"Cooling: safe for {safeElapsed.TotalSeconds:F0}s of {_policy.MinimumCoolingDuration.TotalSeconds:F0}s minimum.");
            }

            // Hysteresis satisfied — reset and fall through to normal classification.
            _criticalSince = null;
            _safeSince = null;
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

    private bool HasUsableTelemetry(ThermalSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Availability == TelemetryAvailability.Unavailable || snapshot.Readings.Count == 0)
        {
            return false;
        }

        return IsCurrent(snapshot.ObservedAt, now) && snapshot.Readings.All(reading =>
            reading.Celsius >= -273.15m && reading.Confidence is >= 0 and <= 1 && IsCurrent(reading.ObservedAt, now));
    }

    private bool IsCurrent(DateTimeOffset observedAt, DateTimeOffset now) =>
        observedAt <= now && now - observedAt <= _policy.ReadingStalenessWindow;

    private static WorkloadDecision Decision(
        ThermalState state, int concurrency, bool accept, string reason)
        => new(state, concurrency, accept, reason);
}
