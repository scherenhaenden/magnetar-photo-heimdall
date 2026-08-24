using Microsoft.Extensions.DependencyInjection;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;

public static class MediaAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddMediaAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IMediaMetadataReader, MetadataExtractorMediaMetadataReader>();
        services.AddSingleton<IContentHasher, Blake3ContentHasher>();
        return services;
    }
}
