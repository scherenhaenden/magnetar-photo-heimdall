using System.Security.Cryptography;
using System.Text.Json;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Ssh;

/// <summary>Typed JSON-RPC adapter over a pinned SSH connection; never interpolates request data into shell text.</summary>
public sealed class SshRemoteAgentRpc : IRemoteAgentRpcV1RemoteContractsContract
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SshRemoteAgentOptions _options;
    private readonly Func<string, string?> _passphraseProvider;
    private readonly TimeSpan _timeout;

    public SshRemoteAgentRpc(SshRemoteAgentOptions options, Func<string, string?>? passphraseProvider = null, TimeSpan? timeout = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _passphraseProvider = passphraseProvider ?? (_ => null);
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public Task<AgentCapabilitiesRemoteContractsModel> GetCapabilitiesAsync(ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default) =>
        InvokeAsync<AgentCapabilitiesRemoteContractsModel>("getCapabilities", new { version }, cancellationToken);
    public Task<AgentHealthSnapshotRemoteContractsModel> GetHealthAsync(ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default) =>
        InvokeAsync<AgentHealthSnapshotRemoteContractsModel>("getHealth", new { version }, cancellationToken);
    public Task<ThermalSnapshotRemoteContractsModel> GetThermalAsync(IReadOnlyList<string> sensorIds, CancellationToken cancellationToken = default) =>
        InvokeAsync<ThermalSnapshotRemoteContractsModel>("getThermal", new { sensorIds }, cancellationToken);
    public async Task<OperationReceiptRemoteContractsModel> SubmitOperationAsync(OperationRequestRemoteContractsModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var capabilities = await GetCapabilitiesAsync(request.Protocol, cancellationToken).ConfigureAwait(false);
        request.ValidateAgainst(capabilities);
        return await InvokeAsync<OperationReceiptRemoteContractsModel>("submitOperation", request, cancellationToken).ConfigureAwait(false);
    }
    public Task<OperationReceiptRemoteContractsModel> GetReceiptAsync(string receiptId, ProtocolVersionRemoteContractsModel version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(receiptId)) throw new ArgumentException("Receipt id is required.", nameof(receiptId));
        return InvokeAsync<OperationReceiptRemoteContractsModel>("getReceipt", new { receiptId, version }, cancellationToken);
    }

    private async Task<T> InvokeAsync<T>(string method, object payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestToken = SshRpcWireCodec.EncodeRequest(new RpcRequest(method, payload));
        var passphrase = _passphraseProvider(_options.PrivateKeyPath);
        using var key = string.IsNullOrEmpty(passphrase)
            ? new PrivateKeyFile(_options.PrivateKeyPath)
            : new PrivateKeyFile(_options.PrivateKeyPath, passphrase);
        using var client = new SshClient(new ConnectionInfo(_options.Host, _options.Port, _options.Username,
            new PrivateKeyAuthenticationMethod(_options.Username, key)));
        var fingerprintAccepted = false;
        client.HostKeyReceived += (_, e) =>
        {
            var actual = "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=');
            fingerprintAccepted = CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(actual), System.Text.Encoding.ASCII.GetBytes(_options.ExpectedHostKeySha256));
            e.CanTrust = fingerprintAccepted;
        };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);
        try
        {
            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!fingerprintAccepted) throw new SshException("SSH host key did not match the configured SHA-256 pin.");
            // Only bounded base64url request data is added to the trusted configured command.
            using var command = client.CreateCommand($"{_options.AgentCommand} {requestToken}");
            command.CommandTimeout = _timeout;
            var asyncResult = command.BeginExecute();
            using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
            using var registration = ioCts.Token.Register(() =>
            {
                try { command.CancelAsync(); } catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or SshException) { }
                try { if (client.IsConnected) client.Disconnect(); } catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or SshException) { }
            });
            var stdoutTask = ReadBoundedAndCancelOnOverflowAsync(command.OutputStream, SshRpcWireCodec.MaximumResponseBytes, ioCts);
            var stderrTask = ReadBoundedAndCancelOnOverflowAsync(command.ExtendedOutputStream, SshRpcWireCodec.MaximumErrorBytes, ioCts);
            var streams = await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            command.EndExecute(asyncResult);
            if (command.ExitStatus != 0) throw new SshException($"Remote Heimdall agent failed with exit code {command.ExitStatus}.");
            var stdout = new System.Text.UTF8Encoding(false, true).GetString(streams[0]);
            var response = JsonSerializer.Deserialize<RpcResponse<T>>(stdout, JsonOptions)
                ?? throw new InvalidDataException("Remote agent returned an empty or invalid response.");
            if (!response.Success) throw new SshException("Remote agent rejected the request. Check the NAS-side agent log for details.");
            return response.Result ?? throw new InvalidDataException("Remote agent response did not contain the requested result.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"SSH remote-agent request exceeded {_timeout}.");
        }
        finally { if (client.IsConnected) client.Disconnect(); }
    }

    private sealed record RpcRequest(string Method, object Payload);
    private sealed record RpcResponse<T>(bool Success, T? Result, string? Error);

    private static async Task<byte[]> ReadBoundedAndCancelOnOverflowAsync(Stream stream, int maximumBytes, CancellationTokenSource cancellation)
    {
        try { return await SshRpcWireCodec.ReadBoundedAsync(stream, maximumBytes, cancellation.Token).ConfigureAwait(false); }
        catch (InvalidDataException)
        {
            cancellation.Cancel();
            throw;
        }
    }
}
