using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Mappers;

internal static class MetadataEvidenceBusinessLogicMapper
{
    public static MetadataEvidenceBusinessLogicModel FromFilesystemMtime(string filePath, DateTimeOffset value, double confidence) =>
        new(filePath, DateSourceBusinessLogicModel.FilesystemMtime, value, confidence, null);
}
