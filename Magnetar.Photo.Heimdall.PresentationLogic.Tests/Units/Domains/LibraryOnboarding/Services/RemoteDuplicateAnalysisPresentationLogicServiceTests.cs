using System.Text.Json;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.RemoteDuplicateAnalysis.Services;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;
using Xunit;

namespace Magnetar.Photo.Heimdall.PresentationLogic.Tests.Units.Domains.RemoteDuplicateAnalysis.Services;

public sealed class RemoteDuplicateAnalysisPresentationLogicServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AnalyzeAsync_SubmitsDuplicateScanForConfiguredRootAndDeserializesCompletedResult()
    {
        var analysis = new LibraryDuplicateAnalysisBusinessLogicModel(
            Guid.NewGuid(), 4, 2, 2, 0, 2,
            [new ExactDuplicateGroupBusinessLogicModel(12, "BLAKE3", 1, new string('a', 64), ["a.jpg", "b.jpg"])],
            []);
        var receipt = Receipt(OperationStatusRemoteContractsModel.Completed, JsonSerializer.Serialize(analysis, JsonOptions), "stable-retry-key");
        var remote = new FakeRemoteAgent(receipt);
        var service = new RemoteDuplicateAnalysisPresentationLogicService(remote);

        var result = await service.AnalyzeAsync("nas_photos", 3, "stable-retry-key");

        Assert.Equal(OperationKindRemoteContractsModel.DuplicateScan, remote.SubmittedRequest!.Kind);
        Assert.Equal("nas_photos", remote.SubmittedRequest.RootId);
        Assert.Equal(string.Empty, remote.SubmittedRequest.RelativePath);
        Assert.Equal(3, remote.SubmittedRequest.RequestedConcurrency);
        Assert.Equal("stable-retry-key", remote.SubmittedRequest.IdempotencyKey);
        var actualAnalysis = Assert.IsType<LibraryDuplicateAnalysisBusinessLogicModel>(result.Analysis);
        Assert.Equal(analysis.LibraryId, actualAnalysis.LibraryId);
        Assert.Equal(analysis.CataloguedAssetCount, actualAnalysis.CataloguedAssetCount);
        Assert.Equal(analysis.Groups[0].FullHash, actualAnalysis.Groups[0].FullHash);
        Assert.Equal(receipt, result.Receipt);
    }

    [Fact]
    public async Task AnalyzeAsync_PropagatesRejectedStatusAndReasonWithoutAnalysis()
    {
        var receipt = Receipt(OperationStatusRemoteContractsModel.Rejected, null, "rejected-key") with
        {
            Reason = "NAS worker could not read the catalogued file."
        };
        var service = new RemoteDuplicateAnalysisPresentationLogicService(new FakeRemoteAgent(receipt));

        var result = await service.AnalyzeAsync("nas_photos", 1, "rejected-key");

        Assert.Equal(OperationStatusRemoteContractsModel.Rejected, result.Receipt.Status);
        Assert.Equal(receipt.Reason, result.Receipt.Reason);
        Assert.Null(result.Analysis);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task AnalyzeAsync_RejectsConcurrencyOutsideAgentCapabilitiesBeforeSubmitting(int concurrency)
    {
        var idempotencyKey = "invalid-concurrency";
        var remote = new FakeRemoteAgent(Receipt(OperationStatusRemoteContractsModel.PausedThermal, null, idempotencyKey));
        var service = new RemoteDuplicateAnalysisPresentationLogicService(remote);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.AnalyzeAsync("nas_photos", concurrency, idempotencyKey));

        Assert.Null(remote.SubmittedRequest);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsMalformedCompletedResult()
    {
        var remote = new FakeRemoteAgent(Receipt(OperationStatusRemoteContractsModel.Completed, "{invalid", "invalid-result"));
        var service = new RemoteDuplicateAnalysisPresentationLogicService(remote);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.AnalyzeAsync("nas_photos", 1, "invalid-result"));
    }

    private static OperationReceiptRemoteContractsModel Receipt(
        OperationStatusRemoteContractsModel status,
        string? resultJson,
        string idempotencyKey) =>
        new("receipt-1", idempotencyKey, status, DateTimeOffset.UtcNow, 1, ResultJson: resultJson);

    private sealed class FakeRemoteAgent(OperationReceiptRemoteContractsModel receipt)
        : IRemoteAgentRpcV1RemoteContractsContract
    {
        public OperationRequestRemoteContractsModel? SubmittedRequest { get; private set; }

        public Task<AgentCapabilitiesRemoteContractsModel> GetCapabilitiesAsync(
            ProtocolVersionRemoteContractsModel version,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ProtocolVersionRemoteContractsModel.V1, version);
            return Task.FromResult(new AgentCapabilitiesRemoteContractsModel(
                ProtocolVersionRemoteContractsModel.V1,
                [new AgentRootRemoteContractsModel("nas_photos", "NAS Photos")],
                [],
                new HashSet<OperationKindRemoteContractsModel> { OperationKindRemoteContractsModel.DuplicateScan },
                4,
                true));
        }

        public Task<AgentHealthSnapshotRemoteContractsModel> GetHealthAsync(
            ProtocolVersionRemoteContractsModel version,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ThermalSnapshotRemoteContractsModel> GetThermalAsync(
            IReadOnlyList<string> sensorIds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OperationReceiptRemoteContractsModel> SubmitOperationAsync(
            OperationRequestRemoteContractsModel request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubmittedRequest = request;
            return Task.FromResult(receipt);
        }

        public Task<OperationReceiptRemoteContractsModel> GetReceiptAsync(
            string receiptId,
            ProtocolVersionRemoteContractsModel version,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
