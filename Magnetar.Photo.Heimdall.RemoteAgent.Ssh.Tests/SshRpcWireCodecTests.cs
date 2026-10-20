using System.Text;
using Xunit;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Ssh.Tests;

public sealed class SshRpcWireCodecTests
{
    [Fact]
    public void RejectsOversizedRequestsBeforeEncodingThemForTheShell()
    {
        var payload = new { data = new string('x', SshRpcWireCodec.MaximumRequestBytes) };
        Assert.Throws<InvalidDataException>(() => SshRpcWireCodec.EncodeRequest(payload));
    }

    [Fact]
    public async Task ReadsStreamsUpToConfiguredBound()
    {
        var expected = Encoding.UTF8.GetBytes("bounded payload");
        await using var stream = new MemoryStream(expected);

        var actual = await SshRpcWireCodec.ReadBoundedAsync(stream, expected.Length, CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task RejectsStreamsThatExceedConfiguredBound()
    {
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SshRpcWireCodec.ReadBoundedAsync(stream, 3, CancellationToken.None));
    }
}
