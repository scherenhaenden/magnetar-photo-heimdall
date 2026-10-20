using Xunit;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Magnetar.Photo.Heimdall.RemoteAgent.Host;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Services;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Host.Tests;

public sealed class RemoteAgentTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ExpectedPaths = ["library/a/one.jpg", "library/b/two.jpg"];

    [Fact]
    public void RpcResponseLimitMatchesSshTransportMaximum()
    {
        Assert.Equal(32 * 1024 * 1024, RemoteAgent.MaximumRpcResponseBytes);
    }

    [Fact]
    public async Task OversizedDuplicateResultReturnsSmallRejectedReceiptWithoutPersistingResultJson()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(temp, "library");
        Directory.CreateDirectory(library);
        try
        {
            for (var i = 0; i < 40; i++)
            {
                var name = $"{i:D3}-" + new string('p', 140) + ".jpg";
                await File.WriteAllBytesAsync(Path.Combine(library, name), [1, 2, 3, 4]);
            }

            var journal = Path.Combine(temp, "journal", "agent.sqlite");
            using var agent = new RemoteAgent(new AgentConfiguration(journal, 2,
                [new AgentRootConfiguration("photos", "Photos", library)], CatalogPath: Path.Combine(temp, "catalog.sqlite")),
                HealthyThermal, maximumRpcResponseBytes: 4096);
            var request = new OperationRequestRemoteContractsModel(ProtocolVersionRemoteContractsModel.V1, "photos", string.Empty,
                OperationKindRemoteContractsModel.DuplicateScan, 2, "oversized-result-key");
            var raw = await agent.DispatchAsync(JsonSerializer.Serialize(new { method = "submitOperation", payload = request }, JsonOptions));
            using var response = JsonDocument.Parse(JsonSerializer.Serialize(raw, JsonOptions));
            var receipt = response.RootElement.GetProperty("result");
            Assert.Equal((int)OperationStatusRemoteContractsModel.Rejected, receipt.GetProperty("status").GetInt32());
            Assert.Null(receipt.GetProperty("resultJson").GetString());
            Assert.Contains("exceeds the 4096-byte RPC response limit", receipt.GetProperty("reason").GetString());

            await using var connection = new SqliteConnection($"Data Source={journal}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT length(ReceiptJson) FROM Receipts WHERE IdempotencyKey = $key;";
            command.Parameters.AddWithValue("$key", request.IdempotencyKey);
            Assert.InRange((long)(await command.ExecuteScalarAsync())!, 1L, 2048L);

            await using var catalog = new SqliteConnection($"Data Source={Path.Combine(temp, "catalog.sqlite")}");
            await catalog.OpenAsync();
            await using var count = catalog.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM assets;";
            Assert.Equal(40L, (long)(await count.ExecuteScalarAsync())!);
            Assert.Equal(40, Directory.GetFiles(library).Length);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task OperationSlotsEnforceAggregateLimitAcrossAgentInstances()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var firstAgent = CreateAgent(temp, temp);
            using var secondAgent = CreateAgent(temp, temp);
            using var thirdAgent = CreateAgent(temp, temp);
            var first = await firstAgent.AcquireOperationSlotAsync();
            using var second = await secondAgent.AcquireOperationSlotAsync();
            var pending = thirdAgent.AcquireOperationSlotAsync();
            Assert.False(pending.IsCompleted);
            first.Dispose();
            using var third = await pending.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task ThermalMonitorCancelsWorkWhenTemperatureBecomesCritical()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var agent = CreateAgent(temp, temp, new WorkloadPolicyRemoteContractsModel(70m, 80m, 90m, 5m,
                TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), 2, 1, 1), () => ThermalAt(95m));
            await using var connection = new SqliteConnection($"Data Source={Path.Combine(temp, "journal", "agent.sqlite")}");
            await connection.OpenAsync();
            using var work = new CancellationTokenSource();
            using var monitor = new CancellationTokenSource();
            var reduced = false;
            await agent.MonitorThermalAsync(connection, 2, work, () => reduced = true, monitor.Token);
            Assert.True(work.IsCancellationRequested);
            Assert.True(reduced);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task DuplicateScanGroupsOnlyEqualSizedIdenticalFilesAndReturnsPathsRelativeToRoot()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(temp, "library");
        Directory.CreateDirectory(Path.Combine(library, "a"));
        Directory.CreateDirectory(Path.Combine(library, "b"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(library, "a", "one.jpg"), [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(Path.Combine(library, "b", "two.jpg"), [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(Path.Combine(library, "different.jpg"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(library, "ignored.txt"), [1, 2, 3, 4]);
            using var agent = new RemoteAgent(new AgentConfiguration(Path.Combine(temp, "journal", "agent.sqlite"), 2,
                [new AgentRootConfiguration("photos", "Photos", temp)]), HealthyThermal);
            var request = new OperationRequestRemoteContractsModel(ProtocolVersionRemoteContractsModel.V1, "photos", string.Empty,
                OperationKindRemoteContractsModel.DuplicateScan, 2, "test-idempotency-key");
            var raw = await agent.DispatchAsync(JsonSerializer.Serialize(new { method = "submitOperation", payload = request }, JsonOptions));
            var responseJson = JsonSerializer.Serialize(raw, JsonOptions);
            using var response = JsonDocument.Parse(responseJson);
            var receipt = response.RootElement.GetProperty("result");
            Assert.Equal((int)OperationStatusRemoteContractsModel.Completed, receipt.GetProperty("status").GetInt32());
            using var analysis = JsonDocument.Parse(receipt.GetProperty("resultJson").GetString()!);
            var groups = analysis.RootElement.GetProperty("groups");
            Assert.Equal(1, groups.GetArrayLength());
            Assert.Equal(2, analysis.RootElement.GetProperty("hashesComputed").GetInt32());
            Assert.Equal(ExpectedPaths,
                groups[0].GetProperty("relativePaths").EnumerateArray().Select(path => path.GetString()).Order().ToArray());
            var duplicate = await agent.DispatchAsync(JsonSerializer.Serialize(new { method = "submitOperation", payload = request }, JsonOptions));
            using var duplicateResponse = JsonDocument.Parse(JsonSerializer.Serialize(duplicate, JsonOptions));
            Assert.Equal(receipt.GetProperty("receiptId").GetString(), duplicateResponse.RootElement.GetProperty("result").GetProperty("receiptId").GetString());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task ConsolidationPlansCopiesVerifiesAndQuarantinesOnlyOnNasWithSqliteReceipt()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-consolidation-test-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(temp, "destination");
        var sourceA = Path.Combine(temp, "source-a");
        var sourceB = Path.Combine(temp, "source-b");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(sourceA);
        Directory.CreateDirectory(sourceB);
        Directory.CreateDirectory(Path.Combine(sourceA, "camera"));
        Directory.CreateDirectory(Path.Combine(sourceB, "camera"));
        var retainedBytes = new byte[] { 1, 2, 3, 4 };
        var missingBytes = new byte[] { 8, 7, 6 };
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(destination, "existing.jpg"), retainedBytes);
            await File.WriteAllBytesAsync(Path.Combine(sourceA, "camera", "duplicate.jpg"), retainedBytes);
            await File.WriteAllBytesAsync(Path.Combine(sourceB, "camera", "duplicate-copy.jpg"), retainedBytes);
            await File.WriteAllBytesAsync(Path.Combine(sourceA, "new.jpg"), missingBytes);
            using var agent = new RemoteAgent(new AgentConfiguration(Path.Combine(temp, "journal", "agent.sqlite"), 2,
                [new AgentRootConfiguration("library", "Target library", destination),
                 new AgentRootConfiguration("src-a", "Source A", sourceA),
                 new AgentRootConfiguration("src-b", "Source B", sourceB)], CatalogPath: Path.Combine(temp, "catalog.sqlite")), HealthyThermal);
            var profile = new OrganizerProfileRemoteContractsModel(
                [],
                "nodate", "unknown-camera", "{name}{extension}", OrganizerCollisionPolicyRemoteContractsModel.Fail);

            var plan = await SubmitConsolidation(agent, OperationKindRemoteContractsModel.ConsolidationPlan, "consolidation-preview", profile);
            Assert.Equal((int)OperationStatusRemoteContractsModel.Completed, plan.GetProperty("status").GetInt32());
            var planResult = JsonDocument.Parse(plan.GetProperty("resultJson").GetString()!).RootElement;
            Assert.Equal("Preview", planResult.GetProperty("phase").GetString());
            Assert.Contains(planResult.GetProperty("items").EnumerateArray(), item => item.GetProperty("action").GetString() == "AlreadyInDestination");
            Assert.Contains(planResult.GetProperty("items").EnumerateArray(), item => item.GetProperty("action").GetString() == "CopyRequired");
            Assert.False(File.Exists(Path.Combine(destination, "new.jpg")));
            Assert.True(File.Exists(Path.Combine(sourceA, "new.jpg")));
            await File.WriteAllBytesAsync(Path.Combine(sourceB, "added-after-preview.jpg"), [4, 4, 4, 4, 4]);

            var copyIds = planResult.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("action").GetString() == "CopyRequired")
                .Select(item => item.GetProperty("planItemId").GetString()!).ToArray();
            var copy = await SubmitConsolidation(agent, OperationKindRemoteContractsModel.ConsolidationCopy, "consolidation-copy", profile,
                planId: "consolidation-preview", selectedPlanItemIds: copyIds);
            Assert.Equal((int)OperationStatusRemoteContractsModel.Completed, copy.GetProperty("status").GetInt32());
            Assert.Equal(1, JsonDocument.Parse(copy.GetProperty("resultJson").GetString()!).RootElement.GetProperty("filesCopied").GetInt32());
            Assert.Equal(missingBytes, await File.ReadAllBytesAsync(Path.Combine(destination, "new.jpg")));
            Assert.False(File.Exists(Path.Combine(destination, "added-after-preview.jpg")));
            Assert.True(File.Exists(Path.Combine(sourceA, "new.jpg")));

            var quarantineIds = planResult.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("action").GetString() is "AlreadyInDestination" or "CopyRequired" or "DuplicateSource")
                .Select(item => item.GetProperty("planItemId").GetString()!).ToArray();
            var quarantine = await SubmitConsolidation(agent, OperationKindRemoteContractsModel.ConsolidationQuarantine, "consolidation-quarantine", profile,
                planId: "consolidation-preview", selectedPlanItemIds: quarantineIds);
            Assert.Equal((int)OperationStatusRemoteContractsModel.Completed, quarantine.GetProperty("status").GetInt32());
            Assert.False(File.Exists(Path.Combine(sourceA, "camera", "duplicate.jpg")));
            Assert.False(File.Exists(Path.Combine(sourceB, "camera", "duplicate-copy.jpg")));
            Assert.True(File.Exists(Path.Combine(sourceA, ".heimdall-quarantine", "consolidation-quarantine", "camera", "duplicate.jpg")));
            Assert.True(File.Exists(Path.Combine(sourceB, ".heimdall-quarantine", "consolidation-quarantine", "camera", "duplicate-copy.jpg")));
            Assert.True(File.Exists(Path.Combine(destination, "existing.jpg")));
            Assert.True(File.Exists(Path.Combine(destination, "new.jpg")));

            await using var db = new SqliteConnection($"Data Source={Path.Combine(temp, "journal", "agent.sqlite")}");
            await db.OpenAsync();
            await using var count = db.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM ConsolidationRuns;";
            Assert.Equal(3L, (long)(await count.ExecuteScalarAsync())!);
            await using var hashes = db.CreateCommand();
            hashes.CommandText = "SELECT COUNT(*) FROM ConsolidationHashes WHERE Algorithm='BLAKE3';";
            Assert.True((long)(await hashes.ExecuteScalarAsync())! >= 4L);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task ConsolidationRefusesDestinationPathCollisionWithoutOverwriting()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-consolidation-conflict-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(temp, "destination");
        var source = Path.Combine(temp, "source");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(source);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(destination, "same.jpg"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(source, "same.jpg"), [9, 8, 7]);
            using var agent = new RemoteAgent(new AgentConfiguration(Path.Combine(temp, "journal", "agent.sqlite"), 1,
                [new AgentRootConfiguration("library", "Target", destination), new AgentRootConfiguration("source", "Source", source)]), HealthyThermal);
            var profile = SafeOrganizerProfile();
            var plan = await SubmitConsolidation(agent, OperationKindRemoteContractsModel.ConsolidationPlan, "path-collision-plan", profile, ["source"]);
            var planResult = JsonDocument.Parse(plan.GetProperty("resultJson").GetString()!).RootElement;
            var conflict = Assert.Single(planResult.GetProperty("items").EnumerateArray());
            Assert.Equal("PathConflict", conflict.GetProperty("action").GetString());
            var result = await SubmitConsolidation(agent, OperationKindRemoteContractsModel.ConsolidationCopy, "path-collision", profile, ["source"],
                "path-collision-plan", [conflict.GetProperty("planItemId").GetString()!]);
            Assert.Equal((int)OperationStatusRemoteContractsModel.Rejected, result.GetProperty("status").GetInt32());
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(destination, "same.jpg")));
            Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(Path.Combine(source, "same.jpg")));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task ConsolidationRejectsAPlannedSourceWhoseContentChangedAfterPreview()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-consolidation-stale-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(temp, "destination");
        var source = Path.Combine(temp, "source");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(source);
        var sourcePath = Path.Combine(source, "new.jpg");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
            var originalTime = new DateTime(2024, 4, 5, 6, 7, 8, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(sourcePath, originalTime);
            using var agent = new RemoteAgent(new AgentConfiguration(Path.Combine(temp, "journal", "agent.sqlite"), 1,
                [new AgentRootConfiguration("library", "Target", destination), new AgentRootConfiguration("source", "Source", source)]), HealthyThermal);
            var profile = SafeOrganizerProfile();
            var plan = await SubmitConsolidation(agent, OperationKindRemoteContractsModel.ConsolidationPlan, "stale-plan", profile, ["source"]);
            var planResult = JsonDocument.Parse(plan.GetProperty("resultJson").GetString()!).RootElement;
            var item = Assert.Single(planResult.GetProperty("items").EnumerateArray());
            await File.WriteAllBytesAsync(sourcePath, [9, 8, 7, 6]);
            File.SetLastWriteTimeUtc(sourcePath, originalTime);
            var result = await SubmitConsolidation(agent, OperationKindRemoteContractsModel.ConsolidationCopy, "stale-copy", profile, ["source"],
                "stale-plan", [item.GetProperty("planItemId").GetString()!]);
            Assert.Equal((int)OperationStatusRemoteContractsModel.Completed, result.GetProperty("status").GetInt32());
            var execution = JsonDocument.Parse(result.GetProperty("resultJson").GetString()!).RootElement;
            Assert.Contains(execution.GetProperty("items").EnumerateArray(), entry => entry.GetProperty("action").GetString() == "PlanStale");
            Assert.False(File.Exists(Path.Combine(destination, "new.jpg")));
            Assert.True(File.Exists(sourcePath));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task RejectsPathTraversalBeforeScanning()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var agent = new RemoteAgent(new AgentConfiguration(Path.Combine(temp, "agent.sqlite"), 1,
                [new AgentRootConfiguration("photos", "Photos", temp)]));
            var request = new OperationRequestRemoteContractsModel(ProtocolVersionRemoteContractsModel.V1, "photos", "../outside",
                OperationKindRemoteContractsModel.DuplicateScan, 1, "unsafe-path-key");
            var raw = await agent.DispatchAsync(JsonSerializer.Serialize(new { method = "submitOperation", payload = request }, JsonOptions));
            var json = JsonSerializer.Serialize(raw, JsonOptions);
            Assert.Contains("Traversal", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task FailsClosedWhenThermalTelemetryIsUnavailable()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "library"));
        try
        {
            using var agent = new RemoteAgent(new AgentConfiguration(Path.Combine(temp, "agent.sqlite"), 1,
                [new AgentRootConfiguration("photos", "Photos", temp)]));
            var request = new OperationRequestRemoteContractsModel(ProtocolVersionRemoteContractsModel.V1, "photos", string.Empty,
                OperationKindRemoteContractsModel.DuplicateScan, 1, "thermal-unavailable-key");
            var raw = await agent.DispatchAsync(JsonSerializer.Serialize(new { method = "submitOperation", payload = request }, JsonOptions));
            using var response = JsonDocument.Parse(JsonSerializer.Serialize(raw, JsonOptions));
            Assert.Equal((int)OperationStatusRemoteContractsModel.PausedThermal,
                response.RootElement.GetProperty("result").GetProperty("status").GetInt32());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task RehashesSameSizeSameMtimeReplacementBeforeConfirmingGroups()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(temp, "library");
        Directory.CreateDirectory(library);
        try
        {
            var firstFile = Path.Combine(library, "one.jpg");
            var secondFile = Path.Combine(library, "two.jpg");
            await File.WriteAllBytesAsync(firstFile, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(secondFile, [1, 2, 3, 4]);
            var fixedTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(firstFile, fixedTime);
            File.SetLastWriteTimeUtc(secondFile, fixedTime);
            using var agent = CreateAgent(temp, library);

            var initial = await SubmitDuplicateScan(agent, "same-initial");
            Assert.Equal(1, initial.GetProperty("groups").GetArrayLength());

            await File.WriteAllBytesAsync(secondFile, [9, 8, 7, 6]);
            File.SetLastWriteTimeUtc(secondFile, fixedTime);
            using var restartedAgent = CreateAgent(temp, library); // Reopen the same persistent catalog and verify root registration is stable.
            var refreshed = await SubmitDuplicateScan(restartedAgent, "same-metadata-replacement");
            Assert.Equal(0, refreshed.GetProperty("groups").GetArrayLength());
            Assert.Equal(0, refreshed.GetProperty("hashesReused").GetInt32());
            Assert.Equal(2, refreshed.GetProperty("hashesComputed").GetInt32());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task RescanRemovesMissingAssetsFromTheCatalogWithoutTouchingFiles()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(temp, "library");
        Directory.CreateDirectory(library);
        var photo = Path.Combine(library, "gone.jpg");
        try
        {
            await File.WriteAllBytesAsync(photo, [1, 2, 3, 4]);
            using var agent = CreateAgent(temp, library);
            _ = await SubmitDuplicateScan(agent, "before-delete");
            File.Delete(photo);

            var result = await SubmitDuplicateScan(agent, "after-delete");
            Assert.Equal(0, result.GetProperty("cataloguedAssetCount").GetInt32());
            Assert.Equal(0, result.GetProperty("groups").GetArrayLength());
            Assert.False(File.Exists(photo));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void RejectsConfiguredRootThatIsASymlink()
    {
        if (OperatingSystem.IsWindows()) return;
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        var actual = Path.Combine(temp, "actual");
        var linked = Path.Combine(temp, "linked");
        Directory.CreateDirectory(actual);
        Directory.CreateSymbolicLink(linked, actual);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => CreateAgent(temp, linked));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void RejectsConfiguredRootWithSymlinkedAncestor()
    {
        if (OperatingSystem.IsWindows()) return;
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        var actual = Path.Combine(temp, "actual");
        var child = Path.Combine(actual, "photos");
        var linked = Path.Combine(temp, "linked");
        Directory.CreateDirectory(child);
        Directory.CreateSymbolicLink(linked, actual);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => CreateAgent(temp, Path.Combine(linked, "photos")));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task CoolingHysteresisSurvivesAgentProcessRestart()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(temp, "library");
        Directory.CreateDirectory(library);
        var policy = new WorkloadPolicyRemoteContractsModel(70m, 80m, 90m, 5m,
            TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(60), 2, 1, 1);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(library, "photo.jpg"), [1, 2, 3]);
            using (var hotAgent = CreateAgent(temp, library, policy, () => ThermalAt(95m)))
                Assert.Equal(OperationStatusRemoteContractsModel.PausedThermal,
                    await SubmitAndGetStatus(hotAgent, "cooldown-critical"));

            // A fresh RemoteAgent instance represents the next SSH process. Safe temperature
            // starts the continuous cooldown, but must not clear the persisted Critical marker.
            using (var firstCoolAgent = CreateAgent(temp, library, policy, () => ThermalAt(40m)))
                Assert.Equal(OperationStatusRemoteContractsModel.PausedThermal,
                    await SubmitAndGetStatus(firstCoolAgent, "cooldown-start"));

            await Task.Delay(300);
            using var cooledAgent = CreateAgent(temp, library, policy, () => ThermalAt(40m));
            Assert.Equal(OperationStatusRemoteContractsModel.Completed,
                await SubmitAndGetStatus(cooledAgent, "cooldown-finished"));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task ObservationGapLongerThanStalenessRestartsCooldownAcrossProcesses()
    {
        var temp = Path.Combine(TestTempPath, "heimdall-agent-test-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(temp, "library");
        Directory.CreateDirectory(library);
        var policy = new WorkloadPolicyRemoteContractsModel(70m, 80m, 90m, 5m,
            TimeSpan.FromMilliseconds(600), TimeSpan.FromSeconds(2), 2, 1, 1);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(library, "photo.jpg"), [1, 2, 3]);
            using (var hotAgent = CreateAgent(temp, library, policy, () => ThermalAt(95m)))
                Assert.Equal(OperationStatusRemoteContractsModel.PausedThermal,
                    await SubmitAndGetStatus(hotAgent, "gap-critical"));

            using (var firstCoolAgent = CreateAgent(temp, library, policy, () => ThermalAt(40m)))
                Assert.Equal(OperationStatusRemoteContractsModel.PausedThermal,
                    await SubmitAndGetStatus(firstCoolAgent, "gap-start-cooldown"));

            // The gap is longer than both staleness and the cooling duration. A fresh
            // safe reading must begin a new cooldown rather than reuse the old SafeSince.
            await Task.Delay(2100);
            using (var afterGapAgent = CreateAgent(temp, library, policy, () => ThermalAt(40m)))
                Assert.Equal(OperationStatusRemoteContractsModel.PausedThermal,
                    await SubmitAndGetStatus(afterGapAgent, "gap-restart-cooldown"));

            await Task.Delay(250);
            using (var stillCoolingAgent = CreateAgent(temp, library, policy, () => ThermalAt(40m)))
                Assert.Equal(OperationStatusRemoteContractsModel.PausedThermal,
                    await SubmitAndGetStatus(stillCoolingAgent, "gap-still-cooling"));

            await Task.Delay(250);
            using (var stillCoolingAgent = CreateAgent(temp, library, policy, () => ThermalAt(40m)))
                Assert.Equal(OperationStatusRemoteContractsModel.PausedThermal,
                    await SubmitAndGetStatus(stillCoolingAgent, "gap-still-cooling-again"));

            await Task.Delay(150);
            using var cooledAgent = CreateAgent(temp, library, policy, () => ThermalAt(40m));
            Assert.Equal(OperationStatusRemoteContractsModel.Completed,
                await SubmitAndGetStatus(cooledAgent, "gap-cooldown-finished"));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void RestoredSafeSinceWithoutLastObservationCannotReleaseCoolingGate()
    {
        var policy = new WorkloadPolicyRemoteContractsModel(70m, 80m, 90m, 5m,
            TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), 2, 1, 1);
        var controller = new ThermalWorkloadRemoteContractsService(policy);
        var now = DateTimeOffset.UtcNow;
        controller.RestoreState(new ThermalWorkloadStateSnapshotRemoteContractsModel(
            CriticalSince: now.AddMinutes(-10),
            SafeSince: now.AddMinutes(-5),
            LastObservedAt: null));

        var snapshot = new ThermalSnapshotRemoteContractsModel(TelemetryAvailabilityRemoteContractsModel.Available, now,
            [new ThermalReadingRemoteContractsModel("test-cpu", 40m, now, 1m, ThermalStateRemoteContractsModel.Normal)]);
        var decision = controller.Observe(snapshot, now);

        Assert.Equal(ThermalStateRemoteContractsModel.Critical, decision.State);
        Assert.Equal(0, decision.EffectiveConcurrency);
        Assert.False(decision.AcceptNewWork);
        Assert.NotNull(controller.CaptureState().CriticalSince);
        Assert.Equal(now, controller.CaptureState().SafeSince);
    }

    private static string TestTempPath => OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();

    private static RemoteAgent CreateAgent(string temp, string library) => new(
        new AgentConfiguration(Path.Combine(temp, "journal", "agent.sqlite"), 2,
            [new AgentRootConfiguration("photos", "Photos", library)], CatalogPath: Path.Combine(temp, "catalog.sqlite")), HealthyThermal);

    private static RemoteAgent CreateAgent(string temp, string library, WorkloadPolicyRemoteContractsModel policy,
        Func<ThermalSnapshotRemoteContractsModel> thermalProvider) => new(
        new AgentConfiguration(Path.Combine(temp, "journal", "agent.sqlite"), 2,
            [new AgentRootConfiguration("photos", "Photos", library)], CatalogPath: Path.Combine(temp, "catalog.sqlite"), WorkloadPolicy: policy), thermalProvider);

    private static async Task<OperationStatusRemoteContractsModel> SubmitAndGetStatus(RemoteAgent agent, string key)
    {
        var request = new OperationRequestRemoteContractsModel(ProtocolVersionRemoteContractsModel.V1, "photos", string.Empty,
            OperationKindRemoteContractsModel.DuplicateScan, 1, key);
        var raw = await agent.DispatchAsync(JsonSerializer.Serialize(new { method = "submitOperation", payload = request }, JsonOptions));
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(raw, JsonOptions));
        return (OperationStatusRemoteContractsModel)response.RootElement.GetProperty("result").GetProperty("status").GetInt32();
    }

    private static ThermalSnapshotRemoteContractsModel ThermalAt(decimal celsius)
    {
        var now = DateTimeOffset.UtcNow;
        return new ThermalSnapshotRemoteContractsModel(TelemetryAvailabilityRemoteContractsModel.Available, now,
            [new ThermalReadingRemoteContractsModel("test-cpu", celsius, now, 1m, ThermalStateRemoteContractsModel.Normal)]);
    }

    private static async Task<JsonElement> SubmitDuplicateScan(RemoteAgent agent, string idempotencyKey)
    {
        var request = new OperationRequestRemoteContractsModel(ProtocolVersionRemoteContractsModel.V1, "photos", string.Empty,
            OperationKindRemoteContractsModel.DuplicateScan, 2, idempotencyKey);
        var raw = await agent.DispatchAsync(JsonSerializer.Serialize(new { method = "submitOperation", payload = request }, JsonOptions));
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(raw, JsonOptions));
        var receipt = response.RootElement.GetProperty("result");
        Assert.Equal((int)OperationStatusRemoteContractsModel.Completed, receipt.GetProperty("status").GetInt32());
        return JsonDocument.Parse(receipt.GetProperty("resultJson").GetString()!).RootElement.Clone();
    }

    private static OrganizerProfileRemoteContractsModel SafeOrganizerProfile() => new(
        [],
        "nodate", "unknown-camera", "{name}{extension}", OrganizerCollisionPolicyRemoteContractsModel.Fail);

    private static async Task<JsonElement> SubmitConsolidation(RemoteAgent agent, OperationKindRemoteContractsModel kind, string key,
        OrganizerProfileRemoteContractsModel profile, string[]? sourceRootIds = null, string? planId = null, string[]? selectedPlanItemIds = null)
    {
        var request = new OperationRequestRemoteContractsModel(ProtocolVersionRemoteContractsModel.V1, "library", string.Empty,
            kind, 1, key, sourceRootIds ?? ["src-a", "src-b"], profile, planId, selectedPlanItemIds);
        var raw = await agent.DispatchAsync(JsonSerializer.Serialize(new { method = "submitOperation", payload = request }, JsonOptions));
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(raw, JsonOptions));
        return response.RootElement.GetProperty("result").Clone();
    }

    private static ThermalSnapshotRemoteContractsModel HealthyThermal()
    {
        var now = DateTimeOffset.UtcNow;
        return new ThermalSnapshotRemoteContractsModel(TelemetryAvailabilityRemoteContractsModel.Available, now,
            [new ThermalReadingRemoteContractsModel("test-cpu", 40m, now, 1m, ThermalStateRemoteContractsModel.Normal)]);
    }
}
