using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Models;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Mappers;

namespace Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;

public interface IMediaFileScannerDataAccessIoService
{
    IAsyncEnumerable<DiscoveredMediaFileDataAccessIoModel> ScanAsync(string rootPath, CancellationToken cancellationToken = default);
}

public sealed class PhysicalMediaFileScannerDataAccessIoService : IMediaFileScannerDataAccessIoService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".tif", ".tiff", ".dng", ".nef", ".cr2", ".arw", ".raf", ".mp4", ".mov"
    };

    public async IAsyncEnumerable<DiscoveredMediaFileDataAccessIoModel> ScanAsync(string rootPath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SupportedExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            var info = new FileInfo(path);
            yield return DiscoveredMediaFileDataAccessIoMapper.FromFileInfo(root, info);
            await Task.Yield();
        }
    }
}
