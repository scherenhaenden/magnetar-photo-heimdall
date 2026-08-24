using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Mappers;

internal static class MetadataEvidenceMapper
{
    public static MetadataEvidence FromFilesystemMtime(string filePath, DateTimeOffset value, double confidence) =>
        new(filePath, DateSource.FilesystemMtime, value, confidence, null);
}
