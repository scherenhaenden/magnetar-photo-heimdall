using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Mappers;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;

/// <summary>Walks media files without following symbolic links or reparse points.</summary>
public sealed class PhysicalMediaFileScannerDataAccessIoService : IMediaFileScannerDataAccessIoService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".tif", ".tiff", ".dng", ".nef", ".cr2", ".arw", ".raf", ".mp4", ".mov"
    };

    public async IAsyncEnumerable<DiscoveredMediaFileDataAccessIoModel> ScanAsync(
        string rootPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Media library root does not exist: {root}");
        EnsureNoReparsePointsInPath(root);

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        while (pendingDirectories.TryPop(out var currentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Recheck before enumeration so a directory replaced with a symlink is not traversed.
            if (IsReparsePoint(currentDirectory))
                continue;

            foreach (var path in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pendingDirectories.Push(path);
                    continue;
                }

                if (!SupportedExtensions.Contains(Path.GetExtension(path)))
                    continue;

                var info = new FileInfo(path);
                yield return DiscoveredMediaFileDataAccessIoMapper.FromFileInfo(root, info);
                await Task.Yield();
            }
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>Rejects a configured root if any existing path component is a symlink or reparse point.</summary>
    public static void EnsureNoReparsePointsInPath(string path)
    {
        DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Exists && IsReparsePoint(current.FullName))
                throw new UnauthorizedAccessException("Media library root cannot traverse a symbolic link or reparse point.");
            current = current.Parent;
        }
    }
}
