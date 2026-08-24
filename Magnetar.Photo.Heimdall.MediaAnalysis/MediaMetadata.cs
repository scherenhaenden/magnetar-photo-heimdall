using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Magnetar.Photo.Heimdall.MediaAnalysis;

public enum CapturedAtSource
{
    Exif,
    Xmp,
    QuickTime,
    FileModifiedTime
}

public sealed record CapturedAt(DateTimeOffset Value, CapturedAtSource Source);

public interface IMediaMetadataReader
{
    ValueTask<CapturedAt> ReadCapturedAtAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Resolves the first usable creation date in the order EXIF, XMP, QuickTime, then file mtime.</summary>
public sealed class MediaMetadataReader : IMediaMetadataReader
{
    private const int MaximumMetadataBytes = 16 * 1024 * 1024;
    private static readonly Regex XmpDate = new(
        "(?:xmp:CreateDate|photoshop:DateCreated|exif:DateTimeOriginal)\\s*=\\s*[\\\"'](?<date>[^\\\"']+)[\\\"']",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async ValueTask<CapturedAt> ReadCapturedAtAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await ReadMetadataPrefixAsync(path, cancellationToken).ConfigureAwait(false);

        if (TryReadExif(bytes, out var exif)) return new(exif, CapturedAtSource.Exif);
        if (TryReadXmp(bytes, out var xmp)) return new(xmp, CapturedAtSource.Xmp);
        if (TryReadQuickTime(bytes, out var quickTime)) return new(quickTime, CapturedAtSource.QuickTime);

        return new(new DateTimeOffset(File.GetLastWriteTimeUtc(path)), CapturedAtSource.FileModifiedTime);
    }

    private static async Task<byte[]> ReadMetadataPrefixAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var count = (int)Math.Min(stream.Length, MaximumMetadataBytes);
        var result = new byte[count];
        var offset = 0;
        while (offset < result.Length)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            offset += read;
        }
        return offset == result.Length ? result : result[..offset];
    }

    private static bool TryReadExif(byte[] data, out DateTimeOffset value)
    {
        value = default;
        var marker = Encoding.ASCII.GetBytes("Exif\0\0");
        var exif = Find(data, marker);
        if (exif < 0 || exif + marker.Length + 8 > data.Length) return false;
        var tiff = exif + marker.Length;
        var littleEndian = data[tiff] == (byte)'I' && data[tiff + 1] == (byte)'I';
        if (!littleEndian && !(data[tiff] == (byte)'M' && data[tiff + 1] == (byte)'M')) return false;
        var ifdOffset = ReadUInt32(data, tiff + 4, littleEndian);
        if (ifdOffset > int.MaxValue || tiff + (int)ifdOffset + 2 > data.Length) return false;
        return TryReadIfdDate(data, tiff, tiff + (int)ifdOffset, littleEndian, out value, includeExifSubIfd: true);
    }

    private static bool TryReadIfdDate(byte[] data, int tiff, int ifd, bool littleEndian, out DateTimeOffset value, bool includeExifSubIfd)
    {
        value = default;
        var entries = ReadUInt16(data, ifd, littleEndian);
        for (var i = 0; i < entries; i++)
        {
            var entry = ifd + 2 + (i * 12);
            if (entry + 12 > data.Length) return false;
            var tag = ReadUInt16(data, entry, littleEndian);
            if ((tag == 0x9003 || tag == 0x0132) && TryParseExifDate(ReadExifAscii(data, tiff, entry, littleEndian), out value)) return true;
            if (includeExifSubIfd && tag == 0x8769)
            {
                var subOffset = ReadUInt32(data, entry + 8, littleEndian);
                if (subOffset <= int.MaxValue && tiff + (int)subOffset + 2 <= data.Length &&
                    TryReadIfdDate(data, tiff, tiff + (int)subOffset, littleEndian, out value, includeExifSubIfd: false)) return true;
            }
        }
        return false;
    }

    private static string ReadExifAscii(byte[] data, int tiff, int entry, bool littleEndian)
    {
        var count = ReadUInt32(data, entry + 4, littleEndian);
        if (count == 0 || count > 128) return string.Empty;
        var start = count <= 4 ? entry + 8 : tiff + (int)ReadUInt32(data, entry + 8, littleEndian);
        return start < 0 || start + count > data.Length ? string.Empty : Encoding.ASCII.GetString(data, start, (int)count).TrimEnd('\0');
    }

    private static bool TryParseExifDate(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);

    private static bool TryReadXmp(byte[] data, out DateTimeOffset value)
    {
        value = default;
        var match = XmpDate.Match(Encoding.UTF8.GetString(data));
        return match.Success && DateTimeOffset.TryParse(match.Groups["date"].Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    private static bool TryReadQuickTime(byte[] data, out DateTimeOffset value)
    {
        value = default;
        for (var index = 4; index + 16 <= data.Length; index++)
        {
            if (data[index] != (byte)'m' || data[index + 1] != (byte)'v' || data[index + 2] != (byte)'h' || data[index + 3] != (byte)'d') continue;
            var atom = index - 4;
            var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(atom, 4));
            if (size < 20 || (ulong)atom + size > (ulong)data.Length) continue;
            var version = data[index + 4];
            long seconds;
            if (version == 0 && atom + 16 <= data.Length) seconds = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(atom + 12, 4));
            else if (version == 1 && atom + 20 <= data.Length) seconds = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(atom + 12, 8));
            else continue;
            try { value = DateTimeOffset.UnixEpoch.AddSeconds(seconds - 2_082_844_800L); return true; } catch (ArgumentOutOfRangeException) { }
        }
        return false;
    }

    private static int Find(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++) if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }
    private static ushort ReadUInt16(byte[] buffer, int offset, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2)) : BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset, 2));
    private static uint ReadUInt32(byte[] buffer, int offset, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4)) : BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset, 4));
}
