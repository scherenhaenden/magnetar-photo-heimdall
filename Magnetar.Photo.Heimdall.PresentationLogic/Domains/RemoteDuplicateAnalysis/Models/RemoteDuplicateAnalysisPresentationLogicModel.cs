using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.PresentationLogic.Domains.RemoteDuplicateAnalysis.Models;

/// <summary>Remote receipt and, when completed, the NAS-side duplicate analysis.</summary>
public sealed record RemoteDuplicateAnalysisPresentationLogicModel(
    OperationReceiptRemoteContractsModel Receipt,
    LibraryDuplicateAnalysisBusinessLogicModel? Analysis);
