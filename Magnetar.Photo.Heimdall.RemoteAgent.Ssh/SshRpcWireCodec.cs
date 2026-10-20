using System.Text;
using System.Text.Json;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Ssh;

/// <summary>Applies explicit bounds to data crossing the SSH command channel.</summary>
internal static class SshRpcWireCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal const int MaximumRequestBytes = 1024 * 1024;
    internal const int MaximumResponseBytes = 32 * 1024 * 1024;
    internal const int MaximumErrorBytes = 16 * 1024;

    internal static string EncodeRequest<T>(T request)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaximumRequestBytes)
            throw new InvalidDataException($"SSH RPC request exceeds the {MaximumRequestBytes}-byte limit.");
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) return output.ToArray();
            if (output.Length + read > maximumBytes)
                throw new InvalidDataException($"SSH RPC stream exceeds the {maximumBytes}-byte limit.");
            output.Write(buffer, 0, read);
        }
    }
}
