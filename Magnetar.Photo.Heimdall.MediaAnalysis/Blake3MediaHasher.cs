using Blake3;

namespace Magnetar.Photo.Heimdall.MediaAnalysis;

public sealed record MediaHashes(string Full, string Quick, long Length);

public interface IMediaHasher
{
    ValueTask<MediaHashes> HashAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Computes a complete BLAKE3 digest and a fast digest of the first and last 1 MiB.</summary>
public sealed class Blake3MediaHasher : IMediaHasher
{
    private const int BufferSize = 128 * 1024;
    private const int QuickWindowSize = 1024 * 1024;

    public async ValueTask<MediaHashes> HashAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        using var full = Hasher.New();
        using var quick = Hasher.New();
        quick.Update(BitConverter.GetBytes(length));
        var buffer = new byte[BufferSize];
        long position = 0;
        var tailStart = Math.Max(QuickWindowSize, length - QuickWindowSize);
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            full.Update(buffer.AsSpan(0, read));
            var firstLength = (int)Math.Clamp(QuickWindowSize - position, 0, read);
            if (firstLength > 0) quick.Update(buffer.AsSpan(0, firstLength));
            var tailOffset = (int)Math.Clamp(tailStart - position, 0, read);
            if (tailOffset < read && position + tailOffset >= QuickWindowSize)
                quick.Update(buffer.AsSpan(tailOffset, read - tailOffset));
            position += read;
        }
        return new(full.Finalize().ToString(), quick.Finalize().ToString(), length);
    }
}
