using Microsoft.Extensions.DependencyInjection;

namespace Magnetar.Photo.Heimdall.MediaAnalysis;

public static class MediaAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddMediaAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IMediaMetadataReader, MetadataExtractorMediaMetadataReader>();
        services.AddSingleton<IContentHasher, Blake3ContentHasher>();
        return services;
    }
}
