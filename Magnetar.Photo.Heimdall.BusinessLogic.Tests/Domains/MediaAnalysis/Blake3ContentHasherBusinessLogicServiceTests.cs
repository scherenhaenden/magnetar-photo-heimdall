using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;
using Xunit;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Tests.Domains.MediaAnalysis;

public sealed class Blake3ContentHasherBusinessLogicServiceTests
{
    [Fact]
    public async Task ComputeAsync_ReturnsEvidenceForStableFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heimdall-hash-{Guid.NewGuid():N}.bin");
        var content = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
        await File.WriteAllBytesAsync(path, content);

        try
        {
            var result = await new Blake3ContentHasherBusinessLogicService().ComputeAsync(path);

            Assert.Equal("BLAKE3", result.Algorithm);
            Assert.Equal(content.LongLength, result.FileLength);
            Assert.Equal(64, result.FullHash.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeAsync_UsesFullBLAKE3DigestForEveryFileByte()
    {
        var path = Path.Combine(Path.GetTempPath(), $"heimdall-blake3-vector-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, "abc"u8.ToArray());

        try
        {
            var result = await new Blake3ContentHasherBusinessLogicService().ComputeAsync(path);

            Assert.Equal("6437b3ac38465133ffb63b75273a8db548c558465d79db03fd359c6cd5bd9d85", result.FullHash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(100, 99, 100, false)]
    [InlineData(100, 101, 101, false)]
    [InlineData(100, 100, 99, false)]
    [InlineData(100, 100, 100, true)]
    public void EnsureStableRead_RejectsGrowthTruncationOrDisappearance(
        long expectedLength,
        long bytesRead,
        long finalStreamLength,
        bool fileExists)
    {
        var timestamp = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        if (fileExists && bytesRead == expectedLength && finalStreamLength == expectedLength)
        {
            Blake3ContentHasherBusinessLogicService.EnsureStableRead(
                expectedLength, bytesRead, finalStreamLength, timestamp, timestamp, fileExists);
            return;
        }

        Assert.Throws<IOException>(() => Blake3ContentHasherBusinessLogicService.EnsureStableRead(
            expectedLength, bytesRead, finalStreamLength, timestamp, timestamp, fileExists));
    }

    [Fact]
    public void EnsureStableRead_RejectsModificationTimeChange()
    {
        var initial = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        Assert.Throws<IOException>(() => Blake3ContentHasherBusinessLogicService.EnsureStableRead(
            100, 100, 100, initial, initial.AddTicks(1), fileExists: true));
    }
}
