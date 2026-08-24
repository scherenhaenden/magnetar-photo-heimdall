using Magnetar.Photo.Heimdall.RemoteContracts;

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

static void Verify(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {message}");
}

static void ExpectContractViolation(Action action, string description)
{
    try { action(); }
    catch (Exception ex) when (ex is RemoteContractValidationException or ArgumentException) { return; }
    throw new InvalidOperationException($"Expected validation failure for: {description}");
}

static ThermalSnapshot AvailableSnapshot(decimal celsius, DateTimeOffset observedAt)
    => new(TelemetryAvailability.Available, observedAt,
        [new ThermalReading("cpu0", celsius, observedAt, 1.0m, ThermalState.Normal)]);

static ThermalSnapshot UnavailableSnapshot(string reason = "no sensor")
    => new(TelemetryAvailability.Unavailable, DateTimeOffset.UtcNow, [], reason);

// ---------------------------------------------------------------------------
// Policy: warning=70, high=80, critical=90, hysteresis=5, cooling=2 min,
//         staleness=30 s, concurrency normal=8 / warning=4 / high=1
// ---------------------------------------------------------------------------

var policy = new WorkloadPolicy(
    WarningCelsius: 70m,
    HighCelsius: 80m,
    CriticalCelsius: 90m,
    HysteresisCelsius: 5m,
    MinimumCoolingDuration: TimeSpan.FromMinutes(2),
    ReadingStalenessWindow: TimeSpan.FromSeconds(30),
    NormalConcurrency: 8,
    WarningConcurrency: 4,
    HighConcurrency: 1);

var t0 = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
var controller = new ThermalWorkloadController(policy);

// ---- 1. Normal reading ----
var d1 = controller.Observe(AvailableSnapshot(65m, t0), t0);
Verify(d1.State == ThermalState.Normal, "65 C must be Normal");
Verify(d1.EffectiveConcurrency == 8, "Normal state must use NormalConcurrency=8");
Verify(d1.AcceptNewWork, "Normal state must accept new work");
Console.WriteLine($"[1] Normal: {d1}");

// ---- 2. Warning reading ----
var d2 = controller.Observe(AvailableSnapshot(72m, t0.AddSeconds(10)), t0.AddSeconds(10));
Verify(d2.State == ThermalState.Warning, "72 C must be Warning");
Verify(d2.EffectiveConcurrency == 4, "Warning state must use WarningConcurrency=4");
Verify(d2.AcceptNewWork, "Warning state must still accept new work");
Console.WriteLine($"[2] Warning: {d2}");

// ---- 3. High reading ----
var d3 = controller.Observe(AvailableSnapshot(82m, t0.AddSeconds(20)), t0.AddSeconds(20));
Verify(d3.State == ThermalState.High, "82 C must be High");
Verify(d3.EffectiveConcurrency == 1, "High state must use HighConcurrency=1");
Verify(!d3.AcceptNewWork, "High state must not accept new heavy work");
Console.WriteLine($"[3] High: {d3}");

// ---- 4. Critical — new work paused, concurrency=0 ----
var tCritical = t0.AddSeconds(30);
var d4 = controller.Observe(AvailableSnapshot(91m, tCritical), tCritical);
Verify(d4.State == ThermalState.Critical, "91 C must be Critical");
Verify(d4.EffectiveConcurrency == 0, "Critical must set EffectiveConcurrency=0");
Verify(!d4.AcceptNewWork, "Critical must not accept new work");
Console.WriteLine($"[4] Critical: {d4}");

// ---- 5. Cooling (too soon) — temperature dropped but minimum duration not elapsed ----
// Only 1 min elapsed < MinimumCoolingDuration=2 min; decision reports Critical (Cooling internally)
var tCooling = tCritical.AddMinutes(1);
var d5 = controller.Observe(AvailableSnapshot(60m, tCooling), tCooling);
Verify(d5.State == ThermalState.Critical, "Temperature 60 C at 1 min must still be blocked (Cooling, reported as Critical)");
Verify(d5.EffectiveConcurrency == 0, "Cooling must keep EffectiveConcurrency=0");
Verify(!d5.AcceptNewWork, "Cooling must not accept new work");
Verify(controller.CurrentState == ThermalState.Critical, "CurrentState must be Critical during cooling");
Console.WriteLine($"[5] Cooling (too soon, reported as Critical): {d5}");

// ---- 6. Still Cooling — temp not below hysteresis target (warning 70 - hysteresis 5 = 65) ----
// 3 min >= 2 min but peak=67 > 65 target
var tCooling2 = tCritical.AddMinutes(3);
var d6 = controller.Observe(AvailableSnapshot(67m, tCooling2), tCooling2);
Verify(d6.State == ThermalState.Critical, "Temperature 67 C (> hysteresis target 65) must still be blocked");
Verify(!d6.AcceptNewWork, "Cooling (temp not low enough) must not accept new work");
Console.WriteLine($"[6] Cooling (temp not low enough, reported as Critical): {d6}");

// ---- 7. Normal resume — safe temperature has now remained continuous for 2 min ----
var tSafeStart = tCooling2.AddSeconds(1);
var stillCooling = controller.Observe(AvailableSnapshot(63m, tSafeStart), tSafeStart);
Verify(stillCooling.State == ThermalState.Critical, "The continuous safe interval begins after the unsafe reading");
var tResume = tSafeStart.AddMinutes(2);
var d7 = controller.Observe(AvailableSnapshot(63m, tResume), tResume);
Verify(d7.State == ThermalState.Normal, "63 C after full cooling must be Normal");
Verify(d7.EffectiveConcurrency == 8, "After cooling, Normal concurrency must restore to 8");
Verify(d7.AcceptNewWork, "After cooling, work must be accepted again");
Console.WriteLine($"[7] Resumed Normal: {d7}");

// ---- 8. Unavailable snapshot — no fabricated temperature ----
var d8 = controller.Observe(UnavailableSnapshot("sensor permission denied"), t0.AddMinutes(10));
Verify(d8.State == ThermalState.Unavailable, "Explicit Unavailable snapshot must set Unavailable state");
Verify(d8.EffectiveConcurrency == 0, "Unavailable must not start work without trustworthy telemetry");
Verify(!d8.AcceptNewWork, "Unavailable must not accept new work");
Console.WriteLine($"[8] Unavailable (explicit): {d8}");

// ---- 9. Stale reading — reading older than ReadingStalenessWindow -> Unavailable ----
var freshNow = t0.AddHours(2);
var staleObservedAt = freshNow.AddSeconds(-60); // 60s old > staleness window of 30s
var staleSnapshot = new ThermalSnapshot(
    TelemetryAvailability.Available,
    freshNow,
    [new ThermalReading("cpu0", 65m, staleObservedAt, 1.0m, ThermalState.Normal)]);
var d9 = controller.Observe(staleSnapshot, freshNow);
Verify(d9.State == ThermalState.Unavailable, "Stale reading must be degraded to Unavailable");
Verify(d9.Reason.Contains("stale"), $"Reason must mention stale telemetry, got: '{d9.Reason}'");
Console.WriteLine($"[9] Stale reading -> Unavailable: {d9}");

// ---- 10. A telemetry outage during cooling must not bypass the cooling gate ----
var recoveryController = new ThermalWorkloadController(policy);
var recoveryCritical = recoveryController.Observe(AvailableSnapshot(91m, t0), t0);
Verify(recoveryCritical.State == ThermalState.Critical, "Recovery setup must enter Critical");
var recoveryOutage = recoveryController.Observe(UnavailableSnapshot("sensor disconnected"), t0.AddSeconds(10));
Verify(recoveryOutage.State == ThermalState.Critical && recoveryOutage.EffectiveConcurrency == 0,
    "An outage during cooling must retain the work pause");
var recoverySafe = recoveryController.Observe(AvailableSnapshot(64m, t0.AddSeconds(20)), t0.AddSeconds(20));
Verify(recoverySafe.State == ThermalState.Critical, "Recovered telemetry must restart the continuous safe interval");
var recoveryResumed = recoveryController.Observe(AvailableSnapshot(64m, t0.AddMinutes(3)), t0.AddMinutes(3));
Verify(recoveryResumed.State == ThermalState.Normal, "Recovery may resume only after the new safe interval");
Console.WriteLine("[10] Cooling gate survives telemetry outage.");

// ---- 11. UnavailableThermalProvider — never fabricates Celsius ----
var unavailableProvider = new UnavailableThermalProvider("No SMC access on this runner.");
var providerSnapshot = unavailableProvider.GetSnapshot();
Verify(providerSnapshot.Availability == TelemetryAvailability.Unavailable, "Provider snapshot must be Unavailable");
Verify(providerSnapshot.Readings.Count == 0, "Provider must return empty reading list — no fabricated Celsius");
Verify(!string.IsNullOrEmpty(providerSnapshot.Reason), "Provider must include a diagnostic reason");
Console.WriteLine($"[11] UnavailableThermalProvider: Availability={providerSnapshot.Availability}, Reason='{providerSnapshot.Reason}'");

// ---- 12. OperationRequest — valid path accepted ----
var caps = new AgentCapabilities(
    ProtocolVersion.V1,
    [new AgentRoot("photos", "Photos Library")],
    [],
    new HashSet<OperationKind> { OperationKind.Hash, OperationKind.Scan },
    8,
    true);

var validOp = new OperationRequest(
    ProtocolVersion.V1, "photos", "2026/trip/DSC0001.nef", OperationKind.Hash, 2, "idem-key-abc");
validOp.ValidateAgainst(caps);
Console.WriteLine("[12] Valid OperationRequest accepted.");

// ---- 13. Absolute paths rejected ----
foreach (var badPath in new[]
{
    "/etc/passwd",
    "C:\\Windows\\system32",
    "C:/Windows/system32",
    "/var/lib/secret"
})
{
    ExpectContractViolation(
        () => new OperationRequest(ProtocolVersion.V1, "photos", badPath, OperationKind.Hash, 1, "k1")
                  .ValidateAgainst(caps),
        $"Absolute path '{badPath}'");
}
Console.WriteLine("[13] Absolute paths correctly rejected.");

// ---- 14. Path traversal rejected ----
foreach (var traversal in new[]
{
    "../secret.nef",
    "2026/../../secret.nef",
    "a/./b",
    "a//b"
})
{
    ExpectContractViolation(
        () => new OperationRequest(ProtocolVersion.V1, "photos", traversal, OperationKind.Hash, 1, "k2")
                  .ValidateAgainst(caps),
        $"Traversal path '{traversal}'");
}
Console.WriteLine("[14] Path traversal correctly rejected.");

// ---- 15. Unknown root rejected ----
ExpectContractViolation(
    () => new OperationRequest(ProtocolVersion.V1, "unknown-root", "image.nef", OperationKind.Hash, 1, "k3")
              .ValidateAgainst(caps),
    "Unknown root 'unknown-root'");
Console.WriteLine("[15] Unknown root correctly rejected.");

// ---- 16. Protocol version mismatch rejected ----
ExpectContractViolation(
    () => new OperationRequest(new ProtocolVersion(2, 0), "photos", "image.nef", OperationKind.Hash, 1, "k4")
              .ValidateAgainst(caps),
    "Protocol major version mismatch");
Console.WriteLine("[16] Protocol version mismatch correctly rejected.");

// ---- 17. Empty idempotency key rejected ----
ExpectContractViolation(
    () => new OperationRequest(ProtocolVersion.V1, "photos", "image.nef", OperationKind.Hash, 1, "")
              .ValidateAgainst(caps),
    "Empty idempotency key");
Console.WriteLine("[17] Empty idempotency key correctly rejected.");

// ---- 18. Concurrency out of range rejected ----
ExpectContractViolation(
    () => new OperationRequest(ProtocolVersion.V1, "photos", "image.nef", OperationKind.Hash, 0, "k5")
              .ValidateAgainst(caps),
    "Concurrency=0");
ExpectContractViolation(
    () => new OperationRequest(ProtocolVersion.V1, "photos", "image.nef", OperationKind.Hash, 99, "k6")
              .ValidateAgainst(caps),
    "Concurrency exceeds MaximumConcurrency");
Console.WriteLine("[18] Out-of-range concurrency correctly rejected.");

// ---- 19. WorkloadPolicy validation catches bad configuration ----
ExpectContractViolation(
    () => new WorkloadPolicy(90m, 80m, 70m, 5m, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), 8, 4, 1).Validate(),
    "Inverted thresholds");
ExpectContractViolation(
    () => new WorkloadPolicy(70m, 80m, 90m, 5m, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), 1, 4, 1).Validate(),
    "WarningConcurrency > NormalConcurrency");
ExpectContractViolation(
    () => new WorkloadPolicy(70m, 80m, 90m, 70m, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30), 8, 4, 1).Validate(),
    "Hysteresis cannot make the safe threshold unreachable");
Console.WriteLine("[19] WorkloadPolicy validation catches bad configuration.");

// ---- 20. AgentRoot id validation ----
ExpectContractViolation(() => new AgentRoot("bad path/root!", "Bad").Validate(), "Root id with invalid chars");
ExpectContractViolation(() => new AgentRoot("", "Empty").Validate(), "Empty root id");
Console.WriteLine("[20] AgentRoot id validation works.");

Console.WriteLine(string.Empty);
Console.WriteLine("PASS: all 20 remote-contract integration assertions passed.");
return 0;
