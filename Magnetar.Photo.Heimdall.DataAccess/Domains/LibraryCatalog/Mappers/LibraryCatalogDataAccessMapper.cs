using Magnetar.Photo.Heimdall.DataAccess.Database.Domains.LibraryCatalog.Models;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Mappers;

internal static class LibraryCatalogDataAccessMapper
{
    public static LibraryRootDataAccessModel ToDataAccessModel(LibraryRootDatabaseModel model) =>
        new(model.Id, model.DisplayName, model.CanonicalPath, model.CreatedAt);

    public static CataloguedAssetDataAccessModel ToDataAccessModel(CataloguedAssetDatabaseModel model) =>
        new(model.Id, model.LibraryId, model.RelativePath, model.Length, model.LastWriteUtc);
}
