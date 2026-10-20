using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;
using Xunit;

namespace Magnetar.Photo.Heimdall.DataAccess.IO.Tests.Domains.FileSystem;

public sealed class PhysicalMediaFileScannerDataAccessIoServiceTests
{
    [Fact]
    public async Task ScanAsync_DoesNotFollowDirectorySymbolicLinks()
    {
        var temp = Path.Combine(TestTempPath, $"heimdall-scan-{Guid.NewGuid():N}");
        var root = Path.Combine(temp, "library");
        var outside = Path.Combine(temp, "outside");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        Directory.CreateDirectory(outside);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "nested", "inside.jpg"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(outside, "outside.jpg"), [4, 5, 6]);
            Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);

            var scanner = new PhysicalMediaFileScannerDataAccessIoService();
            var discovered = new List<string>();
            await foreach (var file in scanner.ScanAsync(root))
                discovered.Add(file.RelativePath);

            Assert.Equal("nested/inside.jpg", Assert.Single(discovered));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static string TestTempPath => OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
}
