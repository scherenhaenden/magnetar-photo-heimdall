namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Durable receipt returned for every submitted OperationRequestRemoteContractsModel.
/// Re-submitting the same IdempotencyKey returns the original receipt.
/// </summary>
public sealed record OperationReceiptRemoteContractsModel(
    string ReceiptId,
    string IdempotencyKey,
    OperationStatusRemoteContractsModel Status,
    DateTimeOffset AcceptedAt,
    int EffectiveConcurrency,
    string? Reason = null);