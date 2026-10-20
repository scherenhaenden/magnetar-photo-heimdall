using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Mappers;

internal static class DiscoveredMediaFileDataAccessIoMapper
{
    public static DiscoveredMediaFileDataAccessIoModel FromFileInfo(string rootPath, FileInfo file)
    {
        // Catalog paths use one portable representation so the same library rows
        // can be consumed by the business layer on Windows, Linux, or macOS.
        var relativePath = Path.GetRelativePath(rootPath, file.FullName)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        return new DiscoveredMediaFileDataAccessIoModel(file.FullName, relativePath, file.Length, file.LastWriteTimeUtc);
    }
}
