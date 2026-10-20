using Xunit;
using Magnetar.Photo.Heimdall.RemoteAgent.Ssh;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Ssh.Tests;

public sealed class SshRemoteAgentOptionsTests
{
    private const string ValidPin = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void ValidatesHostKeyPinAndEndpoint()
    {
        var options = new SshRemoteAgentOptions("nas.example", 22, "heimdall", "/keys/heimdall", ValidPin);
        options.Validate();
    }

    [Theory]
    [InlineData("heimdall-agent rpc; rm -rf /")]
    [InlineData("$(touch /tmp/pwned)")]
    [InlineData("heimdall-agent\nrpc")]
    public void RejectsShellSyntaxInConfiguredAgentCommand(string command)
    {
        var options = new SshRemoteAgentOptions("nas.example", 22, "heimdall", "/keys/heimdall", "SHA256:abc", command);
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void RejectsInvalidSshPort()
    {
        var options = new SshRemoteAgentOptions("nas.example", 0, "heimdall", "/keys/heimdall", ValidPin);
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public async Task HonorsCancellationBeforeOpeningAnyConnection()
    {
        var rpc = new SshRemoteAgentRpc(new SshRemoteAgentOptions("127.0.0.1", 22, "x", "/missing", ValidPin));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rpc.GetHealthAsync(
            Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models.ProtocolVersionRemoteContractsModel.V1,
            cts.Token));
    }

    [Theory]
    [InlineData("SHA256:abc")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    public void RejectsMalformedHostKeyPins(string pin)
    {
        var options = new SshRemoteAgentOptions("nas.example", 22, "heimdall", "/keys/heimdall", pin);
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData("heimdall-agent rpc extra rpc")]
    [InlineData("heimdall-agent  rpc")]
    public void RejectsConfiguredCommandsThatDoNotMatchOneExecutableAndRpc(string command)
    {
        var options = new SshRemoteAgentOptions("nas.example", 22, "heimdall", "/keys/heimdall", ValidPin, command);
        Assert.Throws<ArgumentException>(options.Validate);
    }
}
