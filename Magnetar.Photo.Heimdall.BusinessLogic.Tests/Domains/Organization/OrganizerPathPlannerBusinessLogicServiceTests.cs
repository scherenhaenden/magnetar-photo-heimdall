using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Models;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Services;
using Xunit;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Tests.Domains.Organization;

public sealed class OrganizerPathPlannerBusinessLogicServiceTests
{
    private readonly OrganizerPathPlannerBusinessLogicService planner = new();

    [Fact]
    public void Plan_UsesConfiguredCameraYearMonthAndFilenameTokens()
    {
        var result = planner.Plan(
            Profile(
                [OrganizerFolderSegmentBusinessLogicModel.Camera,
                 OrganizerFolderSegmentBusinessLogicModel.Year,
                 OrganizerFolderSegmentBusinessLogicModel.Month],
                "{year}-{month}_{name}{extension}"),
            "IMG_1234.JPG",
            "Nikon Z 8",
            CaptureDate(new DateTimeOffset(2024, 3, 5, 10, 0, 0, TimeSpan.Zero)));

        Assert.Equal("Nikon Z 8/2024/03/2024-03_IMG_1234.JPG", result.RelativePath);
        Assert.Equal(OrganizerCollisionPolicyBusinessLogicModel.Fail, result.CollisionPolicy);
    }

    [Fact]
    public void Plan_UsesConfiguredFallbacksWhenCameraAndCaptureDateAreMissing()
    {
        var result = planner.Plan(
            Profile(
                [OrganizerFolderSegmentBusinessLogicModel.Camera,
                 OrganizerFolderSegmentBusinessLogicModel.Year,
                 OrganizerFolderSegmentBusinessLogicModel.Month],
                "{name}{extension}"),
            "scan.tif",
            null,
            null);

        Assert.Equal("Unknown camera/No date/scan.tif", result.RelativePath);
    }

    [Fact]
    public void Plan_SanitizesCameraAndSourcePathAndAlwaysReturnsRelativePath()
    {
        var result = planner.Plan(
            Profile([OrganizerFolderSegmentBusinessLogicModel.Camera], "{name}{extension}"),
            "../../family\\one:two.jpg",
            "../Camera/Model",
            null);

        Assert.Equal(".._Camera_Model/one_two.jpg", result.RelativePath);
        Assert.DoesNotContain("../", result.RelativePath, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', result.RelativePath);
        Assert.False(Path.IsPathRooted(result.RelativePath));
    }

    [Fact]
    public void Plan_SupportsDeterministicHashTokenForCollisionDisambiguation()
    {
        var result = planner.Plan(
            Profile([], "{name}-{hash8}{extension}"),
            "IMG_1234.JPG",
            "Camera",
            null,
            "ABCDEF0123456789");

        Assert.Equal("IMG_1234-abcdef01.JPG", result.RelativePath);
    }

    [Fact]
    public void Plan_RequiresValidHashWhenTemplateUsesHashToken()
    {
        Assert.Throws<ArgumentException>(() => planner.Plan(
            Profile([], "{name}-{hash8}{extension}"),
            "IMG_1234.JPG",
            "Camera",
            null));
        Assert.Throws<ArgumentException>(() => planner.Plan(
            Profile([], "{name}-{hash8}{extension}"),
            "IMG_1234.JPG",
            "Camera",
            null,
            "not-a-hash"));
    }

    [Fact]
    public void Plan_RejectsUnknownTemplateTokensAndInvalidSegments()
    {
        Assert.Throws<ArgumentException>(() => planner.Plan(
            Profile([], "{unknown}{extension}"), "a.jpg", "Camera", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(
            Profile([(OrganizerFolderSegmentBusinessLogicModel)77], "{name}{extension}"), "a.jpg", "Camera", null));
    }

    [Fact]
    public void Plan_SanitizesPortableReservedNames()
    {
        var result = planner.Plan(
            Profile([OrganizerFolderSegmentBusinessLogicModel.Camera], "{name}{extension}"),
            "CON.jpg",
            "NUL",
            null);

        Assert.Equal("_NUL/_CON.jpg", result.RelativePath);
    }

    private static OrganizerProfileBusinessLogicModel Profile(
        IReadOnlyList<OrganizerFolderSegmentBusinessLogicModel> segments,
        string template) => new(
            segments,
            "No date",
            "Unknown camera",
            template,
            OrganizerCollisionPolicyBusinessLogicModel.Fail);

    private static ResolvedCaptureDateBusinessLogicModel CaptureDate(DateTimeOffset date) => new(
        date,
        DateSourceBusinessLogicModel.ExifDateTimeOriginal,
        1.0,
        Array.Empty<MetadataEvidenceBusinessLogicModel>());
}
