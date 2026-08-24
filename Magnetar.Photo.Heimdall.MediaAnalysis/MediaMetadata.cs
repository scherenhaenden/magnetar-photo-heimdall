using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Magnetar.Photo.Heimdall.MediaAnalysis;

public enum CapturedAtSource { Exif, Xmp, QuickTime, FileModifiedTime }
public sealed record MetadataEvidence(string Format, string Field, string RawValue, string ParserVersion);
public sealed record ResolvedCaptureDate(DateTimeOffset Value, CapturedAtSource Source, decimal Confidence, MetadataEvidence Evidence);
public interface IMediaMetadataReader { ValueTask<ResolvedCaptureDate> ReadCapturedAtAsync(string path, CancellationToken cancellationToken = default); }

/// <summary>Resolves the first usable creation date in the order EXIF, XMP, QuickTime, then file mtime.</summary>
public sealed class MediaMetadataReader : IMediaMetadataReader
{
    private const int MaximumEmbeddedMetadataBytes = 16 * 1024 * 1024;
    private const string ParserVersion = "1";

    public async ValueTask<ResolvedCaptureDate> ReadCapturedAtAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await ReadMetadataPrefixAsync(path, cancellationToken).ConfigureAwait(false);
        if (TryReadExif(bytes, out var exif, out var exifRaw)) return new(exif, CapturedAtSource.Exif, 1.00m, new("EXIF", "DateTimeOriginal", exifRaw, ParserVersion));
        if (TryReadXmp(bytes, out var xmp, out var xmpField, out var xmpRaw)) return new(xmp, CapturedAtSource.Xmp, 0.95m, new("XMP", xmpField, xmpRaw, ParserVersion));
        var quickTime = await TryReadQuickTimeAsync(path, cancellationToken).ConfigureAwait(false);
        if (quickTime is { } quick) return new(quick, CapturedAtSource.QuickTime, 0.90m, new("QuickTime", "mvhd.creation_time", quick.ToString("O", CultureInfo.InvariantCulture), ParserVersion));
        var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        return new(modified, CapturedAtSource.FileModifiedTime, 0.25m, new("Filesystem", "mtime", modified.ToString("O", CultureInfo.InvariantCulture), ParserVersion));
    }

    private static async Task<byte[]> ReadMetadataPrefixAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[(int)Math.Min(stream.Length, MaximumEmbeddedMetadataBytes)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            offset += read;
        }
        return offset == bytes.Length ? bytes : bytes[..offset];
    }

    private static bool TryReadExif(byte[] data, out DateTimeOffset value, out string raw)
    {
        value = default; raw = string.Empty;
        var marker = "Exif\0\0"u8;
        var exif = Find(data, marker);
        if (exif < 0 || !Contains(data.Length, exif, marker.Length + 8)) return false;
        var tiff = exif + marker.Length;
        var little = data[tiff] == (byte)'I' && data[tiff + 1] == (byte)'I';
        if (!little && !(data[tiff] == (byte)'M' && data[tiff + 1] == (byte)'M')) return false;
        var ifdOffset = ReadUInt32(data, tiff + 4, little);
        if (!TryAdd(tiff, ifdOffset, out var ifd) || !Contains(data.Length, ifd, 2)) return false;
        return TryReadIfdDate(data, tiff, ifd, little, true, out value, out raw);
    }

    private static bool TryReadIfdDate(byte[] data, int tiff, int ifd, bool little, bool includeSubIfd, out DateTimeOffset value, out string raw)
    {
        value = default; raw = string.Empty;
        if (!Contains(data.Length, ifd, 2)) return false;
        var entries = ReadUInt16(data, ifd, little);
        if ((long)ifd + 2 + ((long)entries * 12) > data.Length) return false;
        for (var i = 0; i < entries; i++)
        {
            var entry = ifd + 2 + (i * 12);
            var tag = ReadUInt16(data, entry, little);
            if ((tag == 0x9003 || tag == 0x0132) && TryParseExifDate(ReadExifAscii(data, tiff, entry, little), out value, out raw)) return true;
            if (includeSubIfd && tag == 0x8769)
            {
                var subOffset = ReadUInt32(data, entry + 8, little);
                if (TryAdd(tiff, subOffset, out var subIfd) && TryReadIfdDate(data, tiff, subIfd, little, false, out value, out raw)) return true;
            }
        }
        return false;
    }

    private static string ReadExifAscii(byte[] data, int tiff, int entry, bool little)
    {
        var count = ReadUInt32(data, entry + 4, little);
        if (count is 0 or > 128) return string.Empty;
        var valueOffset = ReadUInt32(data, entry + 8, little);
        var start = count <= 4 ? entry + 8 : TryAdd(tiff, valueOffset, out var resolved) ? resolved : -1;
        return Contains(data.Length, start, (int)count) ? Encoding.ASCII.GetString(data, start, (int)count).TrimEnd('\0') : string.Empty;
    }

    private static bool TryParseExifDate(string text, out DateTimeOffset value, out string raw)
    {
        raw = text;
        return DateTimeOffset.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    private static bool TryReadXmp(byte[] data, out DateTimeOffset value, out string field, out string raw)
    {
        value = default; field = string.Empty; raw = string.Empty;
        var text = Encoding.UTF8.GetString(data);
        var start = text.IndexOf("<x:xmpmeta", StringComparison.OrdinalIgnoreCase);
        var end = text.IndexOf("</x:xmpmeta>", start < 0 ? 0 : start, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end < start) return false;
        try
        {
            var document = XDocument.Parse(text[start..(end + "</x:xmpmeta>".Length)], LoadOptions.None);
            foreach (var element in document.Root!.DescendantsAndSelf())
            {
                var candidates = element.Attributes().Select(a => (Name: a.Name.LocalName, Value: a.Value)).Append((Name: element.Name.LocalName, Value: element.Value));
                foreach (var candidate in candidates)
                {
                    if (candidate.Name is not ("CreateDate" or "DateCreated" or "DateTimeOriginal")) continue;
                    if (DateTimeOffset.TryParse(candidate.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value)) { field = candidate.Name; raw = candidate.Value; return true; }
                }
            }
        }
        catch (System.Xml.XmlException) { }
        return false;
    }

    private static async Task<DateTimeOffset?> TryReadQuickTimeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024 + 7]; var carry = 0; long absolute = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(carry, 64 * 1024), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;
            var total = carry + read;
            for (var i = 4; i + 4 <= total; i++)
            {
                if (!buffer.AsSpan(i, 4).SequenceEqual("mvhd"u8)) continue;
                var parsed = await TryParseMvhdAsync(stream, absolute - carry + i - 4, cancellationToken).ConfigureAwait(false);
                if (parsed is not null) return parsed;
                stream.Position = absolute + read;
            }
            carry = Math.Min(7, total); buffer.AsSpan(total - carry, carry).CopyTo(buffer); absolute += read;
        }
    }

    private static async Task<DateTimeOffset?> TryParseMvhdAsync(FileStream stream, long atomOffset, CancellationToken cancellationToken)
    {
        if (atomOffset < 0 || atomOffset > stream.Length - 20) return null;
        var header = new byte[32]; stream.Position = atomOffset;
        var read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        if (read < 20 || !header.AsSpan(4, 4).SequenceEqual("mvhd"u8)) return null;
        var size32 = BinaryPrimitives.ReadUInt32BigEndian(header); var headerSize = size32 == 1 ? 16 : 8;
        var atomSize = size32 == 1 && read >= 16 ? (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8)) : size32;
        if (atomSize < headerSize + 12 || atomSize > stream.Length - atomOffset) return null;
        var version = header[headerSize]; var creationOffset = version == 0 ? headerSize + 4 : version == 1 ? headerSize + 4 : -1;
        var required = version == 0 ? creationOffset + 4 : creationOffset + 8;
        if (creationOffset < 0 || atomSize < required || read < required) return null;
        var seconds = version == 0 ? BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(creationOffset, 4)) : (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(creationOffset, 8));
        try { return DateTimeOffset.UnixEpoch.AddSeconds(seconds - 2_082_844_800L); } catch (ArgumentOutOfRangeException) { return null; }
    }

    private static int Find(byte[] data, ReadOnlySpan<byte> needle) { for (var i = 0; i <= data.Length - needle.Length; i++) if (data.AsSpan(i, needle.Length).SequenceEqual(needle)) return i; return -1; }
    private static bool Contains(int length, int offset, int count) => offset >= 0 && count >= 0 && (long)offset + count <= length;
    private static bool TryAdd(int offset, uint value, out int sum) { var candidate = (long)offset + value; sum = candidate is >= 0 and <= int.MaxValue ? (int)candidate : -1; return sum >= 0; }
    private static ushort ReadUInt16(byte[] b, int o, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o, 2)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o, 2));
    private static uint ReadUInt32(byte[] b, int o, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
}
