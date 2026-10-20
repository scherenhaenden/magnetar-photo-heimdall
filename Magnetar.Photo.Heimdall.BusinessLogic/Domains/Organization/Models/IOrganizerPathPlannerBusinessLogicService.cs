using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;

namespace Magnetar.Photo.Heimdall.BusinessLogic.Domains.Organization.Models;

/// <summary>Pure path planning from already-extracted media metadata. Performs no file I/O.</summary>
public interface IOrganizerPathPlannerBusinessLogicService
{
    OrganizerPathPlanBusinessLogicModel Plan(
        OrganizerProfileBusinessLogicModel profile,
        string sourceFileName,
        string? camera,
        ResolvedCaptureDateBusinessLogicModel? captureDate,
        string? contentHash = null);
}
