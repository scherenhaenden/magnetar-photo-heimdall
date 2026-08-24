using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Mappers;

internal static class DiscoveredMediaFileMapper
{
    public static DiscoveredMediaFile FromFileInfo(string rootPath, FileInfo file) =>
        new(file.FullName, Path.GetRelativePath(rootPath, file.FullName), file.Length, file.LastWriteTimeUtc);
}
