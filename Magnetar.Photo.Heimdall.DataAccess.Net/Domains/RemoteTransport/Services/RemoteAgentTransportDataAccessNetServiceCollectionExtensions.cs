using Microsoft.Extensions.DependencyInjection;

namespace Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Services;

public static class RemoteAgentTransportDataAccessNetServiceCollectionExtensions
{
    /// <summary>Registers local network-boundary services; it opens no connections.</summary>
    public static IServiceCollection AddRemoteAgentTransportDataAccessNet(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IRemoteAgentTransportDataAccessNetService, RemoteAgentTransportDataAccessNetService>();
        return services;
    }
}
