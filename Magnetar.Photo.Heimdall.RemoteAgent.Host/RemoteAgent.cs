using System.Text.Json;
using System.Collections.Concurrent;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Services;
using Microsoft.Data.Sqlite;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Host;

/// <summary>NAS-side allowlisted RPC implementation. It accepts no shell commands or absolute client paths.</summary>
public sealed class RemoteAgent : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // Keep aligned with SshRpcWireCodec.MaximumResponseBytes. One byte is reserved for Console.WriteLine's newline.
    internal const int MaximumRpcResponseBytes = 32 * 1024 * 1024;
    private readonly AgentConfiguration _configuration;
    private readonly string _journalPath;
    private readonly IReadOnlyDictionary<string, (AgentRootConfiguration Config, string CanonicalPath)> _roots;
    private readonly LibraryCatalogDataAccessService _catalog;
    private readonly LibraryScanBusinessLogicService _libraryScanner;
    private readonly LibraryDuplicateAnalysisBusinessLogicService _duplicateAnalyzer;
    private readonly ConsolidationRemoteService _consolidation;
    private readonly Dictionary<string, LibraryRootDataAccessModel> _registeredLibraries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _libraryInitializationLock = new(1, 1);
    private readonly AgentCapabilitiesRemoteContractsModel _capabilities;
    private readonly Func<ThermalSnapshotRemoteContractsModel> _thermalProvider;
    private readonly ThermalWorkloadRemoteContractsService _thermalController;
    private readonly string _operationLockDirectory;
    private readonly int _maximumRpcResponseBytes;
    private const int MaximumSimultaneousOperations = 2;
    private static readonly TimeSpan ThermalPollInterval = TimeSpan.FromSeconds(1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MacOperationSlots = new(StringComparer.Ordinal);

    public RemoteAgent(AgentConfiguration configuration, Func<ThermalSnapshotRemoteContractsModel>? thermalProvider = null)
        : this(configuration, thermalProvider, MaximumRpcResponseBytes)
    {
    }

    internal RemoteAgent(AgentConfiguration configuration, Func<ThermalSnapshotRemoteContractsModel>? thermalProvider, int maximumRpcResponseBytes)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRpcResponseBytes, 1024);
        _maximumRpcResponseBytes = maximumRpcResponseBytes;
        if (_configuration.MaximumConcurrency is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(configuration), "Concurrency must be 1..64.");
        if (_configuration.Roots.Count == 0) throw new ArgumentException("At least one allowed root must be configured.", nameof(configuration));
        if (string.IsNullOrWhiteSpace(configuration.JournalPath) || !Path.IsPathFullyQualified(configuration.JournalPath))
            throw new ArgumentException("The agent journal path must be absolute.", nameof(configuration));
        _journalPath = Path.GetFullPath(configuration.JournalPath);
        _operationLockDirectory = _journalPath + ".operation-locks";
        _roots = configuration.Roots.ToDictionary(root =>
        {
            new AgentRootRemoteContractsModel(root.Id, root.DisplayName).Validate();
            if (string.IsNullOrWhiteSpace(root.DisplayName) || string.IsNullOrWhiteSpace(root.Path) || !Path.IsPathFullyQualified(root.Path))
                throw new ArgumentException("Every configured root needs a display name and absolute path.", nameof(configuration));
            var canonical = Path.GetFullPath(root.Path);
            if (!Directory.Exists(canonical)) throw new DirectoryNotFoundException($"Configured root '{root.Id}' does not exist.");
            PhysicalMediaFileScannerDataAccessIoService.EnsureNoReparsePointsInPath(canonical);
            return root.Id;
        }, root => (root, Path.GetFullPath(root.Path)), StringComparer.Ordinal);
        if (_roots.Values.Select(root => root.CanonicalPath).Distinct(PathComparer).Count() != _roots.Count)
            throw new ArgumentException("Each root id must map to a distinct canonical directory.", nameof(configuration));
        var configuredCatalogPath = configuration.CatalogPath ?? configuration.JournalPath + ".catalog.sqlite";
        if (string.IsNullOrWhiteSpace(configuredCatalogPath) || !Path.IsPathFullyQualified(configuredCatalogPath))
            throw new ArgumentException("The catalog path must be absolute.", nameof(configuration));
        var catalogPath = Path.GetFullPath(configuredCatalogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        var database = new SqliteLibraryCatalogDataAccessDatabaseService(catalogPath);
        _catalog = new LibraryCatalogDataAccessService(database, new PhysicalMediaFileScannerDataAccessIoService());
        _libraryScanner = new LibraryScanBusinessLogicService(_catalog);
        _duplicateAnalyzer = new LibraryDuplicateAnalysisBusinessLogicService(
            _catalog, new ExactDuplicateCandidateGrouperBusinessLogicService(new Blake3ContentHasherBusinessLogicService()));
        _consolidation = new ConsolidationRemoteService(_roots, _journalPath);
        _thermalProvider = thermalProvider ?? (() => ReadThermalSnapshot(configuration.ThermalSensorDirectory ?? "/sys/class/thermal"));
        var initialThermal = _thermalProvider();
        _capabilities = new AgentCapabilitiesRemoteContractsModel(
            ProtocolVersionRemoteContractsModel.V1,
            _roots.Values.Select(item => new AgentRootRemoteContractsModel(item.Config.Id, item.Config.DisplayName)).ToArray(),
            initialThermal.Readings.Select(reading => new ThermalSensorCapabilityRemoteContractsModel(reading.SensorId, "sysfs", -20m, 150m, 0.1m)).ToArray(),
            new HashSet<OperationKindRemoteContractsModel> { OperationKindRemoteContractsModel.Scan, OperationKindRemoteContractsModel.Hash, OperationKindRemoteContractsModel.DuplicateScan,
                OperationKindRemoteContractsModel.ConsolidationPlan, OperationKindRemoteContractsModel.ConsolidationCopy, OperationKindRemoteContractsModel.ConsolidationQuarantine },
            configuration.MaximumConcurrency,
            true);
        var warningConcurrency = Math.Max(1, configuration.MaximumConcurrency / 2);
        _thermalController = new ThermalWorkloadRemoteContractsService(configuration.WorkloadPolicy ?? new WorkloadPolicyRemoteContractsModel(
            70m, 80m, 90m, 5m, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60),
            configuration.MaximumConcurrency, warningConcurrency, 1));
        InitializeJournal();
    }

    public void Dispose() => _libraryInitializationLock.Dispose();

    public async Task<object> DispatchAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var method = root.GetProperty("method").GetString() ?? throw new InvalidDataException("RPC method is required.");
        var payload = root.GetProperty("payload");
        try
        {
            object result = method switch
            {
                "getCapabilities" => GetCapabilities(payload),
                "getHealth" => GetHealth(payload),
                "getThermal" => GetThermal(payload),
                "submitOperation" => await SubmitOperationAsync(payload),
                "getReceipt" => await GetReceiptAsync(payload),
                _ => throw new InvalidDataException("Unsupported RPC method.")
            };
            var response = new { success = true, result };
            if (FitsRpcResponse(response)) return response;
            return new { success = false, error = "RPC response exceeds the 32 MiB transport limit; the result was not sent." };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private AgentCapabilitiesRemoteContractsModel GetCapabilities(JsonElement payload)
    {
        var version = payload.GetProperty("version").Deserialize<ProtocolVersionRemoteContractsModel>(JsonOptions);
        version.EnsureSupportedBy(_capabilities.Protocol);
        return _capabilities;
    }

    private AgentHealthSnapshotRemoteContractsModel GetHealth(JsonElement payload)
    {
        var version = payload.GetProperty("version").Deserialize<ProtocolVersionRemoteContractsModel>(JsonOptions);
        version.EnsureSupportedBy(_capabilities.Protocol);
        return new AgentHealthSnapshotRemoteContractsModel(AgentHealthStatusRemoteContractsModel.Healthy, DateTimeOffset.UtcNow);
    }

    private ThermalSnapshotRemoteContractsModel GetThermal(JsonElement payload)
    {
        var requested = payload.GetProperty("sensorIds").Deserialize<string[]>(JsonOptions) ?? Array.Empty<string>();
        var snapshot = _thermalProvider();
        if (requested.Length == 0) return snapshot;
        var readings = snapshot.Readings.Where(reading => requested.Contains(reading.SensorId, StringComparer.Ordinal)).ToArray();
        return readings.Length == 0
            ? new ThermalSnapshotRemoteContractsModel(TelemetryAvailabilityRemoteContractsModel.Unavailable, snapshot.ObservedAt, readings, "Requested thermal sensor ids are unavailable.")
            : snapshot with { Readings = readings };
    }

    private async Task<OperationReceiptRemoteContractsModel> SubmitOperationAsync(JsonElement payload)
    {
        var request = payload.Deserialize<OperationRequestRemoteContractsModel>(JsonOptions) ?? throw new InvalidDataException("Operation request is invalid.");
        request.ValidateAgainst(_capabilities);
        await using var connection = new SqliteConnection($"Data Source={_journalPath}");
        await connection.OpenAsync();
        string? previousReceiptId = null;
        await using (var existing = connection.CreateCommand())
        {
            existing.CommandText = "SELECT ReceiptJson FROM Receipts WHERE IdempotencyKey = $key";
            existing.Parameters.AddWithValue("$key", request.IdempotencyKey);
            var saved = await existing.ExecuteScalarAsync();
            if (saved is string receiptJson)
            {
                var savedReceipt = JsonSerializer.Deserialize<OperationReceiptRemoteContractsModel>(receiptJson, JsonOptions)!;
                if (savedReceipt.Status != OperationStatusRemoteContractsModel.PausedThermal) return savedReceipt;
                previousReceiptId = savedReceipt.ReceiptId;
            }
        }

        var accepted = DateTimeOffset.UtcNow;
        string? resultJson = null;
        IDisposable? operationLock = null;
        try
        {
            operationLock = await AcquireOperationSlotAsync();
            if (request.RelativePath.Length != 0)
                throw new InvalidDataException("Remote Scan, Hash, and DuplicateScan operate on the selected configured root; subpaths are not accepted.");
            var thermalSnapshot = _thermalProvider();
            var decision = await ObserveAndPersistThermalStateAsync(connection, thermalSnapshot, DateTimeOffset.UtcNow);
            if (!decision.AcceptNewWork)
            {
                var paused = new OperationReceiptRemoteContractsModel(previousReceiptId ?? Guid.NewGuid().ToString("N"), request.IdempotencyKey,
                    OperationStatusRemoteContractsModel.PausedThermal, accepted, 0, decision.Reason);
                await SaveReceiptAsync(connection, paused, replaceExisting: true);
                return paused;
            }
            var library = await GetOrCreateLibraryAsync(request.RootId);
            var operationConcurrency = Math.Min(request.RequestedConcurrency, decision.EffectiveConcurrency);
            using var operationCancellation = new CancellationTokenSource();
            using var monitorCancellation = new CancellationTokenSource();
            var thermalReduction = 0;
            var monitor = MonitorThermalAsync(connection, operationConcurrency, operationCancellation, () => thermalReduction++, monitorCancellation.Token);
            try
            {
                switch (request.Kind)
                {
                    case OperationKindRemoteContractsModel.Scan:
                        resultJson = SerializeJsonBounded(await _libraryScanner.ScanAsync(library));
                        break;
                    case OperationKindRemoteContractsModel.Hash:
                    case OperationKindRemoteContractsModel.DuplicateScan:
                        resultJson = SerializeJsonBounded(await _duplicateAnalyzer.AnalyzeAsync(library, operationConcurrency, operationCancellation.Token));
                        break;
                    case OperationKindRemoteContractsModel.ConsolidationPlan:
                        resultJson = SerializeJsonBounded(await _consolidation.RunAsync(request, "plan", operationCancellation.Token));
                        break;
                    case OperationKindRemoteContractsModel.ConsolidationCopy:
                        resultJson = SerializeJsonBounded(await _consolidation.RunAsync(request, "copy", operationCancellation.Token));
                        break;
                    case OperationKindRemoteContractsModel.ConsolidationQuarantine:
                        resultJson = SerializeJsonBounded(await _consolidation.RunAsync(request, "quarantine", operationCancellation.Token));
                        break;
                    default: throw new InvalidDataException("This agent only accepts Scan, Hash, and DuplicateScan.");
                }
            }
            finally
            {
                monitorCancellation.Cancel();
                try { await monitor; } catch (OperationCanceledException) { }
            }
            if (thermalReduction > 0)
                throw new OperationCanceledException("Thermal limits changed during analysis; operation stopped safely and can be retried.");
            var completed = new OperationReceiptRemoteContractsModel(previousReceiptId ?? Guid.NewGuid().ToString("N"), request.IdempotencyKey,
                OperationStatusRemoteContractsModel.Completed, accepted, Math.Min(request.RequestedConcurrency, decision.EffectiveConcurrency), ResultJson: resultJson);
            if (!FitsRpcResponse(new { success = true, result = completed }))
            {
                var oversized = new OperationReceiptRemoteContractsModel(completed.ReceiptId, request.IdempotencyKey,
                    OperationStatusRemoteContractsModel.Rejected, accepted, 0,
                    $"Result exceeds the {_maximumRpcResponseBytes}-byte RPC response limit; it was not stored in the receipt. The catalog remains intact.");
                await SaveReceiptAsync(connection, oversized, replaceExisting: true);
                return oversized;
            }
            await SaveReceiptAsync(connection, completed, replaceExisting: true);
            return completed;
        }
        catch (RpcPayloadTooLargeException)
        {
            var oversized = new OperationReceiptRemoteContractsModel(previousReceiptId ?? Guid.NewGuid().ToString("N"), request.IdempotencyKey,
                OperationStatusRemoteContractsModel.Rejected, accepted, 0,
                $"Result exceeds the {_maximumRpcResponseBytes}-byte RPC response limit; it was not stored in the receipt. The catalog remains intact.");
            await SaveReceiptAsync(connection, oversized, replaceExisting: true);
            return oversized;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                var paused = new OperationReceiptRemoteContractsModel(previousReceiptId ?? Guid.NewGuid().ToString("N"), request.IdempotencyKey,
                    OperationStatusRemoteContractsModel.PausedThermal, accepted, 0, ex.Message);
                await SaveReceiptAsync(connection, paused, replaceExisting: true);
                return paused;
            }
            var rejected = new OperationReceiptRemoteContractsModel(previousReceiptId ?? Guid.NewGuid().ToString("N"), request.IdempotencyKey,
                OperationStatusRemoteContractsModel.Rejected, accepted, 0, ex.Message);
            await SaveReceiptAsync(connection, rejected, replaceExisting: true);
            return rejected;
        }
        finally { operationLock?.Dispose(); }
    }

    private string SerializeJsonBounded<T>(T value)
    {
        using var stream = new BoundedMemoryStream(_maximumRpcResponseBytes);
        JsonSerializer.Serialize(stream, value, JsonOptions);
        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private bool FitsRpcResponse<T>(T response)
    {
        try
        {
            using var stream = new BoundedMemoryStream(_maximumRpcResponseBytes - 1);
            JsonSerializer.Serialize(stream, response, JsonOptions);
            return true;
        }
        catch (RpcPayloadTooLargeException) { return false; }
    }

    private sealed class RpcPayloadTooLargeException : IOException { }

    private sealed class BoundedMemoryStream(int maximumLength) : MemoryStream
    {
        private void EnsureCapacityFor(int count)
        {
            if (count < 0 || Length + count > maximumLength) throw new RpcPayloadTooLargeException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityFor(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityFor(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacityFor(1);
            base.WriteByte(value);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureCapacityFor(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            EnsureCapacityFor(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }
    }

    internal async Task<IDisposable> AcquireOperationSlotAsync()
    {
        Directory.CreateDirectory(_operationLockDirectory);
        var slotCount = Math.Min(MaximumSimultaneousOperations, _configuration.MaximumConcurrency);
        if (OperatingSystem.IsMacOS())
        {
            var semaphore = MacOperationSlots.GetOrAdd(_operationLockDirectory, _ => new SemaphoreSlim(slotCount, slotCount));
            await semaphore.WaitAsync();
            return new SemaphoreLease(semaphore);
        }
        while (true)
        {
            for (var index = 0; index < slotCount; index++)
            {
                var stream = new FileStream(Path.Combine(_operationLockDirectory, $"slot-{index}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                try
                {
                    stream.Lock(0, 1);
                    return stream;
                }
                catch (IOException) { stream.Dispose(); }
            }
            await Task.Delay(100);
        }
    }

    internal async Task MonitorThermalAsync(SqliteConnection connection, int activeConcurrency, CancellationTokenSource operationCancellation, Action onReduction, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(ThermalPollInterval, cancellationToken);
            var decision = await ObserveAndPersistThermalStateAsync(connection, _thermalProvider(), DateTimeOffset.UtcNow);
            if (!decision.AcceptNewWork || decision.EffectiveConcurrency < activeConcurrency)
            {
                onReduction();
                operationCancellation.Cancel();
                return;
            }
        }
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }

    private async Task<LibraryRootDataAccessModel> GetOrCreateLibraryAsync(string rootId)
    {
        if (_registeredLibraries.TryGetValue(rootId, out var registered)) return registered;
        await _libraryInitializationLock.WaitAsync();
        try
        {
            if (_registeredLibraries.TryGetValue(rootId, out registered)) return registered;
            await _catalog.InitializeAsync();
            var root = _roots[rootId];
            registered = await _catalog.GetOrCreateLibraryAsync(root.Config.DisplayName, root.CanonicalPath);
            if (!Path.GetFullPath(registered.CanonicalPath).Equals(root.CanonicalPath, PathComparison))
                throw new UnauthorizedAccessException("The SQLite library root does not match the configured root.");
            _registeredLibraries.Add(rootId, registered);
            return registered;
        }
        finally { _libraryInitializationLock.Release(); }
    }

    private async Task<OperationReceiptRemoteContractsModel> GetReceiptAsync(JsonElement payload)
    {
        var version = payload.GetProperty("version").Deserialize<ProtocolVersionRemoteContractsModel>(JsonOptions);
        version.EnsureSupportedBy(_capabilities.Protocol);
        var id = payload.GetProperty("receiptId").GetString() ?? throw new InvalidDataException("Receipt id is required.");
        await using var connection = new SqliteConnection($"Data Source={_journalPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ReceiptJson FROM Receipts WHERE ReceiptId = $id";
        command.Parameters.AddWithValue("$id", id);
        var value = await command.ExecuteScalarAsync();
        if (value is not string receiptJson) throw new KeyNotFoundException("Receipt was not found.");
        return JsonSerializer.Deserialize<OperationReceiptRemoteContractsModel>(receiptJson, JsonOptions)!;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private void InitializeJournal()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
        using var connection = new SqliteConnection($"Data Source={_journalPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS Receipts (ReceiptId TEXT NOT NULL PRIMARY KEY, IdempotencyKey TEXT NOT NULL UNIQUE, ReceiptJson TEXT NOT NULL); CREATE TABLE IF NOT EXISTS ThermalState (Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1), CriticalSinceUtc TEXT NULL, SafeSinceUtc TEXT NULL, LastObservedAtUtc TEXT NULL);";
        command.ExecuteNonQuery();
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(ThermalState);";
        using var reader = columns.ExecuteReader();
        var hasLastObservedAt = false;
        while (reader.Read())
            if (StringComparer.OrdinalIgnoreCase.Equals(reader.GetString(1), "LastObservedAtUtc")) hasLastObservedAt = true;
        reader.Close();
        if (!hasLastObservedAt)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE ThermalState ADD COLUMN LastObservedAtUtc TEXT NULL;";
            migrate.ExecuteNonQuery();
        }
    }

    private static async Task SaveReceiptAsync(SqliteConnection connection, OperationReceiptRemoteContractsModel receipt, bool replaceExisting)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = replaceExisting
            ? "INSERT INTO Receipts (ReceiptId, IdempotencyKey, ReceiptJson) VALUES ($id,$key,$json) ON CONFLICT(IdempotencyKey) DO UPDATE SET ReceiptId=excluded.ReceiptId, ReceiptJson=excluded.ReceiptJson"
            : "INSERT INTO Receipts (ReceiptId, IdempotencyKey, ReceiptJson) VALUES ($id,$key,$json)";
        command.Parameters.AddWithValue("$id", receipt.ReceiptId);
        command.Parameters.AddWithValue("$key", receipt.IdempotencyKey);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(receipt, JsonOptions));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<WorkloadDecisionRemoteContractsModel> ObserveAndPersistThermalStateAsync(
        SqliteConnection connection,
        ThermalSnapshotRemoteContractsModel snapshot,
        DateTimeOffset now)
    {
        // Serialize thermal transitions across short-lived SSH processes, then persist before work starts.
        await using var begin = connection.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE;";
        await begin.ExecuteNonQueryAsync();
        try
        {
            await using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT CriticalSinceUtc, SafeSinceUtc, LastObservedAtUtc FROM ThermalState WHERE Id = 1;";
                await using var reader = await read.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    _thermalController.RestoreState(new ThermalWorkloadStateSnapshotRemoteContractsModel(
                        reader.IsDBNull(0) ? null : DateTimeOffset.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture),
                        reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                        reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            var decision = _thermalController.Observe(snapshot, now);
            var state = _thermalController.CaptureState();
            await using var update = connection.CreateCommand();
            update.CommandText = "INSERT INTO ThermalState (Id, CriticalSinceUtc, SafeSinceUtc, LastObservedAtUtc) VALUES (1, $critical, $safe, $observed) ON CONFLICT(Id) DO UPDATE SET CriticalSinceUtc=excluded.CriticalSinceUtc, SafeSinceUtc=excluded.SafeSinceUtc, LastObservedAtUtc=excluded.LastObservedAtUtc;";
            update.Parameters.AddWithValue("$critical", (object?)state.CriticalSince?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            update.Parameters.AddWithValue("$safe", (object?)state.SafeSince?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            update.Parameters.AddWithValue("$observed", (object?)state.LastObservedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
            await update.ExecuteNonQueryAsync();
            await using var commit = connection.CreateCommand();
            commit.CommandText = "COMMIT;";
            await commit.ExecuteNonQueryAsync();
            return decision;
        }
        catch
        {
            await using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            await rollback.ExecuteNonQueryAsync();
            throw;
        }
    }

    private static ThermalSnapshotRemoteContractsModel ReadThermalSnapshot(string sensorDirectory)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var readings = new List<ThermalReadingRemoteContractsModel>();
        try
        {
            foreach (var zone in Directory.EnumerateDirectories(sensorDirectory, "thermal_zone*"))
            {
                var file = Path.Combine(zone, "temp");
                try
                {
                    if (!long.TryParse(File.ReadAllText(file).Trim(), out var raw)) continue;
                    var celsius = raw > 1000 ? raw / 1000m : raw;
                    if (celsius is < -273.15m or > 200m) continue;
                    var sensorId = Path.GetFileName(zone);
                    readings.Add(new ThermalReadingRemoteContractsModel(sensorId, celsius, observedAt, 0.8m, ThermalStateRemoteContractsModel.Normal));
                }
                catch (IOException) { /* Ignore an unreadable zone; other valid zones still provide telemetry. */ }
                catch (UnauthorizedAccessException) { /* Ignore an unreadable zone; other valid zones still provide telemetry. */ }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ThermalSnapshotRemoteContractsModel(TelemetryAvailabilityRemoteContractsModel.Unavailable, observedAt, Array.Empty<ThermalReadingRemoteContractsModel>(), exception.Message);
        }
        return readings.Count == 0
            ? new ThermalSnapshotRemoteContractsModel(TelemetryAvailabilityRemoteContractsModel.Unavailable, observedAt, readings, "No readable thermal sensor was found.")
            : new ThermalSnapshotRemoteContractsModel(TelemetryAvailabilityRemoteContractsModel.Available, observedAt, readings);
    }
}
