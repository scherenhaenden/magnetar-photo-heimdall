using System.Text.Json;
using Magnetar.Photo.Heimdall.BusinessLogic.Domains.MediaAnalysis.Models;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.RemoteDuplicateAnalysis.Models;
using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

namespace Magnetar.Photo.Heimdall.PresentationLogic.Domains.RemoteDuplicateAnalysis.Services;

/// <summary>Requests read-only duplicate analysis from the configured NAS-side worker.</summary>
public sealed class RemoteDuplicateAnalysisPresentationLogicService(
    IRemoteAgentRpcV1RemoteContractsContract remoteAgent)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the agent's advertised roots and operations without starting a scan.</summary>
    public Task<AgentCapabilitiesRemoteContractsModel> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default) =>
        remoteAgent.GetCapabilitiesAsync(ProtocolVersionRemoteContractsModel.V1, cancellationToken);

    public async Task<RemoteDuplicateAnalysisPresentationLogicModel> AnalyzeAsync(
        string rootId,
        int requestedConcurrency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var capabilities = await remoteAgent.GetCapabilitiesAsync(
            ProtocolVersionRemoteContractsModel.V1, cancellationToken).ConfigureAwait(false);
        if (requestedConcurrency is < 1 || requestedConcurrency > capabilities.MaximumConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedConcurrency), requestedConcurrency,
                $"Concurrency must be between 1 and {capabilities.MaximumConcurrency} for this agent.");
        }

        var request = new OperationRequestRemoteContractsModel(
            ProtocolVersionRemoteContractsModel.V1,
            rootId,
            string.Empty,
            OperationKindRemoteContractsModel.DuplicateScan,
            requestedConcurrency,
            idempotencyKey);
        request.ValidateAgainst(capabilities);

        var receipt = await remoteAgent.SubmitOperationAsync(request, cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(receipt.IdempotencyKey, request.IdempotencyKey))
            throw new InvalidDataException("The remote agent returned a receipt for a different idempotency key.");
        if (receipt.Status != OperationStatusRemoteContractsModel.Completed)
            return new RemoteDuplicateAnalysisPresentationLogicModel(receipt, null);

        if (string.IsNullOrWhiteSpace(receipt.ResultJson))
            throw new InvalidDataException("The remote agent marked duplicate analysis complete without a result payload.");

        LibraryDuplicateAnalysisBusinessLogicModel? analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<LibraryDuplicateAnalysisBusinessLogicModel>(receipt.ResultJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The remote duplicate-analysis result could not be decoded.", exception);
        }

        if (analysis is null)
            throw new InvalidDataException("The remote duplicate-analysis result was empty.");

        return new RemoteDuplicateAnalysisPresentationLogicModel(receipt, analysis);
    }
}
