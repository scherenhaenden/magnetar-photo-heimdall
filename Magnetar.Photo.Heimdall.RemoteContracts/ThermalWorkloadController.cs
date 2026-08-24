namespace Magnetar.Photo.Heimdall.RemoteContracts;

public enum ThermalWorkloadState { Normal, Warning, High, Critical, Cooling, Unavailable }

public sealed record ThermalPolicy(decimal WarningCelsius, decimal HighCelsius, decimal CriticalCelsius, decimal HysteresisCelsius, TimeSpan MinimumCooling, int NormalConcurrency, int WarningConcurrency, int HighConcurrency)
{
    public void Validate()
    {
        if (WarningCelsius >= HighCelsius || HighCelsius >= CriticalCelsius || HysteresisCelsius < 0 || MinimumCooling < TimeSpan.Zero || NormalConcurrency < 1 || WarningConcurrency < 1 || HighConcurrency < 1 || WarningConcurrency > NormalConcurrency || HighConcurrency > WarningConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(ThermalPolicy), "Thermal thresholds and concurrency limits are invalid.");
        }
    }
}

public sealed record WorkloadDecision(ThermalWorkloadState State, int EffectiveConcurrency, bool AcceptNewHeavyWork, string Reason);

/// <summary>Per-host thermal state machine. It never runs remote shell commands.</summary>
public sealed class ThermalWorkloadController
{
    private readonly ThermalPolicy _policy;
    private DateTimeOffset? _criticalSince;
    private ThermalWorkloadState _state = ThermalWorkloadState.Unavailable;

    public ThermalWorkloadController(ThermalPolicy policy)
    {
        policy.Validate();
        _policy = policy;
    }

    public WorkloadDecision Observe(ThermalSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Availability == TelemetryAvailability.Unavailable || snapshot.Readings.Count == 0)
        {
            _state = ThermalWorkloadState.Unavailable;
            return Decision(_state, _policy.WarningConcurrency, true, snapshot.Reason ?? "Thermal telemetry is unavailable.");
        }

        var temperature = snapshot.Readings.Max(reading => reading.Celsius);
        if (temperature >= _policy.CriticalCelsius)
        {
            _criticalSince ??= now;
            _state = ThermalWorkloadState.Critical;
            return Decision(_state, 0, false, "Critical thermal threshold reached.");
        }

        if (_state == ThermalWorkloadState.Critical || _state == ThermalWorkloadState.Cooling)
        {
            var safe = temperature < _policy.WarningCelsius - _policy.HysteresisCelsius;
            if (!safe || now - _criticalSince!.Value < _policy.MinimumCooling)
            {
                _state = ThermalWorkloadState.Cooling;
                return Decision(_state, 0, false, "Waiting for thermal hysteresis and minimum cooling time.");
            }

            _criticalSince = null;
        }

        _state = temperature >= _policy.HighCelsius ? ThermalWorkloadState.High
            : temperature >= _policy.WarningCelsius ? ThermalWorkloadState.Warning
            : ThermalWorkloadState.Normal;
        return _state switch
        {
            ThermalWorkloadState.High => Decision(_state, _policy.HighConcurrency, false, "High thermal threshold reached."),
            ThermalWorkloadState.Warning => Decision(_state, _policy.WarningConcurrency, true, "Warning thermal threshold reached."),
            _ => Decision(_state, _policy.NormalConcurrency, true, "Temperature is within the normal range."),
        };
    }

    private static WorkloadDecision Decision(ThermalWorkloadState state, int concurrency, bool acceptHeavy, string reason) => new(state, concurrency, acceptHeavy, reason);
}
