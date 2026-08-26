using Blake3;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using System.Text;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;

/// <summary>
/// Computes BLAKE3 content hashes for media files using streaming I/O.
/// No file is ever loaded entirely into memory.
/// </summary>
/// <remarks>
/// <b>Full hash</b>: BLAKE3 over every byte of the file.  Uniquely identifies content.<br/>
/// <b>Quick fingerprint (Version 1)</b>: BLAKE3 over <c>Length_LE8 || first_1MiB || last_1MiB</c>.
/// The file length is mixed in first so that padding attacks on small files are impractical.
/// For files ≤ 2 MiB the sampled window equals the full file (same bytes, potentially different hash
/// because the length prefix is always included).
/// </remarks>
public sealed class Blake3ContentHasherBusinessLogicService : IContentHasherBusinessLogicService
{
    /// <summary>Current fingerprint scheme version. Bump when the quick-hash algorithm changes.</summary>
    public const int FingerprintVersion = 1;

    private const int ReadBufferSize   = 128 * 1024;       // 128 KiB per I/O call
    private const long QuickWindowSize = 1024L * 1024;     // 1 MiB head + 1 MiB tail
    private static readonly byte[] QuickDomain = Encoding.ASCII.GetBytes("Magnetar.Photo.Heimdall/quick-fingerprint/v1\0");

    public async ValueTask<HashEvidenceBusinessLogicModel> ComputeAsync(
        string filePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ReadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var fileLength = stream.Length;
        var tailStart  = Math.Max(QuickWindowSize, fileLength - QuickWindowSize);

        using var fullHasher  = Hasher.New();
        using var quickHasher = Hasher.New();

        // Domain-separate the quick scheme and mix its version and file length first.
        quickHasher.Update(QuickDomain);
        Span<byte> lengthBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(lengthBytes, fileLength);
        quickHasher.Update(lengthBytes);

        var buffer   = new byte[ReadBufferSize];
        long position = 0;
        var wroteTailSeparator = false;
        int read;

        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = buffer.AsSpan(0, read);

            // Full hash always covers every byte.
            fullHasher.Update(chunk);

            // Quick fingerprint: head window (bytes 0 .. QuickWindowSize-1)
            var headBytes = (int)Math.Clamp(QuickWindowSize - position, 0L, read);
            if (headBytes > 0)
            {
                quickHasher.Update(chunk[..headBytes]);
            }

            // Quick fingerprint: tail window (bytes tailStart .. end)
            var tailOffset = (int)Math.Clamp(tailStart - position, 0L, read);
            if (tailOffset < read && position + tailOffset >= QuickWindowSize)
            {
                if (!wroteTailSeparator)
                {
                    quickHasher.Update("\0tail\0"u8);
                    wroteTailSeparator = true;
                }
                quickHasher.Update(chunk[tailOffset..]);
            }

            position += read;
        }

        return new HashEvidenceBusinessLogicModel(
            Algorithm:        "BLAKE3",
            QuickFingerprintVersion: FingerprintVersion,
            FullHash:         fullHasher.Finalize().ToString(),
            QuickFingerprint: quickHasher.Finalize().ToString(),
            FileLength:       fileLength);
    }
}
