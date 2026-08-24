using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Magnetar.Photo.Heimdall.DataAccess.Domains.DataAccessComposition.Services;

/// <summary>Composition entry point for DataAccess and its Database, IO and Net sublayers.</summary>
public static class DataAccessServiceCollectionExtensions
{
    public static IServiceCollection AddHeimdallDataAccess(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddLibraryCatalogDataAccess(databasePath)
            .AddRemoteAgentTransportDataAccessNet();
    }
}
