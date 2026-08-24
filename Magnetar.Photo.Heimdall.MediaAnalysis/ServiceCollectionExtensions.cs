using Microsoft.Extensions.DependencyInjection;

namespace Magnetar.Photo.Heimdall.MediaAnalysis;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediaAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IMediaMetadataReader, MediaMetadataReader>();
        services.AddSingleton<IContentHasher, Blake3MediaHasher>();
        return services;
    }
}
