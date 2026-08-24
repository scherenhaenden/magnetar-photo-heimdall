namespace Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Models;

public sealed record DiscoveredMediaFileDataAccessIoModel(string FullPath, string RelativePath, long Length, DateTimeOffset LastWriteUtc);
