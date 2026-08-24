using System.Buffers.Binary;
using System.Text;
using Blake3;

namespace Magnetar.Photo.Heimdall.MediaAnalysis;

public sealed record HashEvidence(
    string Algorithm,
    string AlgorithmVersion,
    string FullHash,
    string QuickFingerprint,
    string QuickFingerprintVersion,
    long Length);

public interface IContentHasher
{
    ValueTask<HashEvidence> HashAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Computes a complete BLAKE3 digest and a versioned quick fingerprint over the first and last MiB.</summary>
public sealed class Blake3MediaHasher : IContentHasher
{
    public const string QuickFingerprintVersion = "heimdall-blake3-quick-v1";
    private const int BufferSize = 128 * 1024;
    private const int QuickWindowSize = 1024 * 1024;
    private static readonly byte[] QuickDomain = Encoding.ASCII.GetBytes("Magnetar.Photo.Heimdall/quick-fingerprint/v1\0");

    public async ValueTask<HashEvidence> HashAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        using var full = Hasher.New();
        using var quick = Hasher.New();
        quick.Update(QuickDomain);
        Span<byte> lengthBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(lengthBytes, length);
        quick.Update(lengthBytes);
        var buffer = new byte[BufferSize];
        long position = 0;
        var tailStart = Math.Max(QuickWindowSize, length - QuickWindowSize);
        var wroteTailSeparator = false;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            full.Update(buffer.AsSpan(0, read));
            var firstLength = (int)Math.Clamp(QuickWindowSize - position, 0, read);
            if (firstLength > 0) quick.Update(buffer.AsSpan(0, firstLength));
            var tailOffset = (int)Math.Clamp(tailStart - position, 0, read);
            if (tailOffset < read && position + tailOffset >= QuickWindowSize)
            {
                if (!wroteTailSeparator) { quick.Update("\0tail\0"u8); wroteTailSeparator = true; }
                quick.Update(buffer.AsSpan(tailOffset, read - tailOffset));
            }
            position += read;
        }
        return new("BLAKE3", "BLAKE3-256", full.Finalize().ToString(), quick.Finalize().ToString(), QuickFingerprintVersion, length);
    }
}
