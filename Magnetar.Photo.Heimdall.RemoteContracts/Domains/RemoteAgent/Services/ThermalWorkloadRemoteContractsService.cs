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
public sealed class ThermalWorkloadRemoteContractsService
{
    private readonly WorkloadPolicyRemoteContractsModel _policy;
    private DateTimeOffset? _criticalSince;
    private DateTimeOffset? _safeSince;
    private ThermalControllerRemoteContractsServiceState _state = ThermalControllerRemoteContractsServiceState.Unavailable;

    public ThermalWorkloadRemoteContractsService(WorkloadPolicyRemoteContractsModel policy)
    {
        policy.Validate();
        _policy = policy;
    }

    /// <summary>
    /// Current public state, exposed for diagnostics.
    /// Cooling maps to ThermalStateRemoteContractsModel.Critical externally (work is still blocked).
    /// </summary>
    public ThermalStateRemoteContractsModel CurrentState => _state switch
    {
        ThermalControllerRemoteContractsServiceState.Normal => ThermalStateRemoteContractsModel.Normal,
        ThermalControllerRemoteContractsServiceState.Warning => ThermalStateRemoteContractsModel.Warning,
        ThermalControllerRemoteContractsServiceState.High => ThermalStateRemoteContractsModel.High,
        ThermalControllerRemoteContractsServiceState.Critical => ThermalStateRemoteContractsModel.Critical,
        ThermalControllerRemoteContractsServiceState.Cooling => ThermalStateRemoteContractsModel.Critical,
        _ => ThermalStateRemoteContractsModel.Unavailable
    };

    /// <summary>
    /// Processes one thermal snapshot and returns the workload decision to apply.
    /// now is injected to keep this method deterministic and unit-testable.
    /// </summary>
    public WorkloadDecisionRemoteContractsModel Observe(ThermalSnapshotRemoteContractsModel snapshot, DateTimeOffset now)
    {
        if (!HasUsableTelemetry(snapshot, now))
        {
            if (_criticalSince is not null)
            {
                _safeSince = null;
                _state = ThermalControllerRemoteContractsServiceState.Cooling;
                return Decision(ThermalStateRemoteContractsModel.Critical, 0, false,
                    snapshot.Reason ?? "Thermal telemetry is unavailable while cooling is required.");
            }

            _state = ThermalControllerRemoteContractsServiceState.Unavailable;
            return Decision(ThermalStateRemoteContractsModel.Unavailable, 0, false,
                snapshot.Reason ?? "Thermal telemetry is unavailable, stale, future-dated, or invalid.");
        }

        var peak = snapshot.Readings.Max(r => r.Celsius);

        // ---- Critical entry ----
        if (peak >= _policy.CriticalCelsius)
        {
            _criticalSince ??= now;
            _safeSince = null;
            _state = ThermalControllerRemoteContractsServiceState.Critical;
            return Decision(ThermalStateRemoteContractsModel.Critical, 0, false,
                $"Critical thermal threshold ({_policy.CriticalCelsius} C) reached: peak {peak} C.");
        }

        // ---- Cooling hysteresis ----
        if (_criticalSince is not null)
        {
            var coolTarget = _policy.WarningCelsius - _policy.HysteresisCelsius;
            if (peak >= coolTarget)
            {
                _safeSince = null;
                _state = ThermalControllerRemoteContractsServiceState.Cooling;
                return Decision(ThermalStateRemoteContractsModel.Critical, 0, false,
                    $"Cooling: peak {peak} C must be below {coolTarget} C.");
            }

            _safeSince ??= now;
            var safeElapsed = now - _safeSince.Value;
            if (safeElapsed < _policy.MinimumCoolingDuration)
            {
                _state = ThermalControllerRemoteContractsServiceState.Cooling;
                return Decision(ThermalStateRemoteContractsModel.Critical, 0, false,
                    $"Cooling: safe for {safeElapsed.TotalSeconds:F0}s of {_policy.MinimumCoolingDuration.TotalSeconds:F0}s minimum.");
            }

            // Hysteresis satisfied — reset and fall through to normal classification.
            _criticalSince = null;
            _safeSince = null;
        }

        // ---- Normal classification ----
        _state = peak >= _policy.HighCelsius ? ThermalControllerRemoteContractsServiceState.High
               : peak >= _policy.WarningCelsius ? ThermalControllerRemoteContractsServiceState.Warning
               : ThermalControllerRemoteContractsServiceState.Normal;

        return _state switch
        {
            ThermalControllerRemoteContractsServiceState.High =>
                Decision(ThermalStateRemoteContractsModel.High, _policy.HighConcurrency, false,
                    $"High thermal threshold ({_policy.HighCelsius} C) reached: peak {peak} C. New heavy work paused."),
            ThermalControllerRemoteContractsServiceState.Warning =>
                Decision(ThermalStateRemoteContractsModel.Warning, _policy.WarningConcurrency, true,
                    $"Warning thermal threshold ({_policy.WarningCelsius} C) reached: peak {peak} C. Concurrency reduced."),
            _ =>
                Decision(ThermalStateRemoteContractsModel.Normal, _policy.NormalConcurrency, true,
                    $"Temperature nominal: peak {peak} C."),
        };
    }

    private bool HasUsableTelemetry(ThermalSnapshotRemoteContractsModel snapshot, DateTimeOffset now)
    {
        if (snapshot.Availability == TelemetryAvailabilityRemoteContractsModel.Unavailable || snapshot.Readings.Count == 0)
        {
            return false;
        }

        return IsCurrent(snapshot.ObservedAt, now) && snapshot.Readings.All(reading =>
            reading.Celsius >= -273.15m && reading.Confidence is >= 0 and <= 1 && IsCurrent(reading.ObservedAt, now));
    }

    private bool IsCurrent(DateTimeOffset observedAt, DateTimeOffset now) =>
        observedAt <= now && now - observedAt <= _policy.ReadingStalenessWindow;

    private static WorkloadDecisionRemoteContractsModel Decision(
        ThermalStateRemoteContractsModel state, int concurrency, bool accept, string reason)
        => new(state, concurrency, accept, reason);
}
