using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;

public interface IMediaFileScannerDataAccessIoService
{
    IAsyncEnumerable<DiscoveredMediaFileDataAccessIoModel> ScanAsync(string rootPath, CancellationToken cancellationToken = default);
}