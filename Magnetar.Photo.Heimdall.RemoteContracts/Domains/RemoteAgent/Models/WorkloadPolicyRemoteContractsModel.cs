namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Policy parameters that govern workload acceptance under thermal pressure.
/// All concurrency values must satisfy: HighConcurrency le WarningConcurrency le NormalConcurrency.
/// </summary>
public sealed record WorkloadPolicyRemoteContractsModel(
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
        if (WarningCelsius <= 0
            || WarningCelsius >= HighCelsius
            || HighCelsius >= CriticalCelsius
            || HysteresisCelsius < 0
            || HysteresisCelsius >= WarningCelsius
            || MinimumCoolingDuration < TimeSpan.Zero
            || ReadingStalenessWindow <= TimeSpan.Zero
            || NormalConcurrency < 1
            || WarningConcurrency < 1
            || HighConcurrency < 1
            || WarningConcurrency > NormalConcurrency
            || HighConcurrency > WarningConcurrency)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WorkloadPolicyRemoteContractsModel),
                "Thermal thresholds and concurrency limits are inconsistent.");
        }
    }
}