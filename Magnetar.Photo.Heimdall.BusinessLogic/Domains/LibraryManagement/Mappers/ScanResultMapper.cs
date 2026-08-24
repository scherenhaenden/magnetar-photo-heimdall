using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Models;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Mappers;

internal static class ScanResultMapper
{
    public static ScanResult FromCatalogueCount(Guid libraryId, int cataloguedAssetCount) =>
        new(libraryId, cataloguedAssetCount);
}
