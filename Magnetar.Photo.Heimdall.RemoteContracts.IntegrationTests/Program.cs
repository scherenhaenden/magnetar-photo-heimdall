using Magnetar.Photo.Heimdall.RemoteContracts;

var policy = new ThermalPolicy(70, 80, 90, 5, TimeSpan.FromMinutes(2), 8, 4, 1);
var controller = new ThermalWorkloadController(policy);
var start = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

Assert(controller.Observe(Snapshot(65), start).State == ThermalWorkloadState.Normal, "Normal readings use normal concurrency.");
Assert(controller.Observe(Snapshot(72), start.AddSeconds(10)).EffectiveConcurrency == 4, "Warning readings reduce concurrency.");
Assert(controller.Observe(Snapshot(82), start.AddSeconds(20)).State == ThermalWorkloadState.High, "High readings stop new heavy work.");
Assert(controller.Observe(Snapshot(91), start.AddSeconds(30)).State == ThermalWorkloadState.Critical, "Critical readings pause work.");
Assert(controller.Observe(Snapshot(60), start.AddMinutes(1)).State == ThermalWorkloadState.Cooling, "Cooling duration prevents immediate resume.");
Assert(controller.Observe(Snapshot(64), start.AddMinutes(3)).State == ThermalWorkloadState.Normal, "Hysteresis plus cooling duration resumes safely.");
Assert(controller.Observe(new ThermalSnapshot(TelemetryAvailability.Unavailable, start, [], "sensor permission denied"), start.AddMinutes(4)).State == ThermalWorkloadState.Unavailable, "Unavailable telemetry is explicit.");

var capabilities = new AgentCapabilities(ProtocolVersion.V1, [new RemoteRoot("photos", "Photos")], [], new HashSet<WorkloadKind> { WorkloadKind.Hash }, 2, true);
var valid = new WorkloadRequest(ProtocolVersion.V1, "photos", "2026/trip/image.nef", WorkloadKind.Hash, 1, "job-1");
valid.ValidateAgainst(capabilities);
foreach (var invalidPath in new[] { "/etc/passwd", "../secret.nef", "2026/../../secret.nef", "C:\\Windows\\system32" })
{
    ExpectValidation(() => new WorkloadRequest(ProtocolVersion.V1, "photos", invalidPath, WorkloadKind.Hash, 1, "job-2").ValidateAgainst(capabilities));
}
ExpectValidation(() => new WorkloadRequest(ProtocolVersion.V1, "other", "image.nef", WorkloadKind.Hash, 1, "job-3").ValidateAgainst(capabilities));
Console.WriteLine("PASS: thermal state sequence and remote path/root contract validation.");

static ThermalSnapshot Snapshot(decimal celsius) => new(TelemetryAvailability.Available, DateTimeOffset.UtcNow, [new ThermalReading("cpu", celsius, DateTimeOffset.UtcNow, 1, "nominal")]);
static void ExpectValidation(Action action) { try { action(); } catch (RemoteContractValidationException) { return; } throw new InvalidOperationException("Expected contract validation failure."); }
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
