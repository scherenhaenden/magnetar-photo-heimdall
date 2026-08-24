using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.IO.Domains.FileSystem.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;

public static class LibraryCatalogDataAccessServiceCollectionExtensions
{
    /// <summary>Registers the real SQLite + physical-filesystem implementation behind the DataAccess facade.</summary>
    public static IServiceCollection AddLibraryCatalogDataAccess(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddSingleton<ILibraryCatalogDatabaseService>(_ => new SqliteLibraryCatalogDatabaseService(databasePath));
        services.AddSingleton<IMediaFileScannerDataAccessIoService, PhysicalMediaFileScannerDataAccessIoService>();
        services.AddSingleton<ILibraryCatalogDataAccessService, LibraryCatalogDataAccessService>();
        return services;
    }
}
