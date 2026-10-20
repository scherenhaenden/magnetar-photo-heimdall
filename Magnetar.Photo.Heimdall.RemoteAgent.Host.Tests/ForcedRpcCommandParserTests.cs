using System.Text;
using System.Text.Json;
using Magnetar.Photo.Heimdall.RemoteAgent.Host;
using Xunit;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Host.Tests;

public sealed class ForcedRpcCommandParserTests
{
    [Fact]
    public void AcceptsOnlyForcedModeArguments()
    {
        Assert.Equal("e30", ForcedRpcCommandParser.ExtractToken(["forced"], "heimdall-agent rpc e30"));
    }

    [Fact]
    public void RejectsDirectRpcAndUnexpectedArguments()
    {
        Assert.Throws<InvalidDataException>(() => ForcedRpcCommandParser.ExtractToken(["rpc", "e30"], "heimdall-agent rpc e30"));
        Assert.Throws<InvalidDataException>(() => ForcedRpcCommandParser.ExtractToken(["forced", "extra"], "heimdall-agent rpc e30"));
        Assert.Throws<InvalidDataException>(() => ForcedRpcCommandParser.ExtractToken([], "heimdall-agent rpc e30"));
    }

    [Fact]
    public void ExtractsOnlyTheExactForcedRpcCommand()
    {
        var json = "{\"method\":\"getHealth\",\"payload\":{}}";
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(token, ForcedRpcCommandParser.ExtractToken("heimdall-agent rpc " + token));
        Assert.Equal(json, ForcedRpcCommandParser.DecodeRequestToken(token));
    }

    [Theory]
    [InlineData("heimdall-agent rpc e30=\n")]
    [InlineData("heimdall-agent rpc e30=\r\n")]
    [InlineData("heimdall-agent rpc e30=;id")]
    [InlineData("heimdall-agent rpc e30= extra")]
    public void RejectsCommandSuffixesAndShellSyntax(string command)
    {
        Assert.Throws<InvalidDataException>(() => ForcedRpcCommandParser.ExtractToken(command));
    }

    [Fact]
    public void RejectsOversizedForcedCommandBeforeParsingToken()
    {
        var command = "heimdall-agent rpc " + new string('A', 1_398_105);

        Assert.Throws<InvalidDataException>(() => ForcedRpcCommandParser.ExtractToken(command));
    }

    [Fact]
    public void RejectsDecodedRequestAboveOneMibibyte()
    {
        var oversizedToken = new string('A', 1_398_104);

        Assert.Throws<InvalidDataException>(() => ForcedRpcCommandParser.DecodeRequestToken(oversizedToken));
    }

    [Fact]
    public void FailureEnvelopeDoesNotRevealExceptionDetails()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var safeJson = ForcedRpcCommandParser.SerializeFailure(options);
        var sensitiveException = new InvalidOperationException("/volume1/private/config.json");

        Assert.DoesNotContain(sensitiveException.Message, safeJson, StringComparison.Ordinal);
        Assert.Contains("Remote agent request failed.", safeJson, StringComparison.Ordinal);
    }
}
