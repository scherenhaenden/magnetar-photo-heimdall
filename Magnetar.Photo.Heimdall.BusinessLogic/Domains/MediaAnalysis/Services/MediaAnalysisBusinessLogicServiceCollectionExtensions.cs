using Microsoft.Extensions.DependencyInjection;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Services;

public static class MediaAnalysisBusinessLogicServiceCollectionExtensions
{
    public static IServiceCollection AddMediaAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IMediaMetadataReaderBusinessLogicService, MetadataExtractorMediaMetadataReaderBusinessLogicService>();
        services.AddSingleton<IContentHasherBusinessLogicService, Blake3ContentHasherBusinessLogicService>();
        return services;
    }
}
