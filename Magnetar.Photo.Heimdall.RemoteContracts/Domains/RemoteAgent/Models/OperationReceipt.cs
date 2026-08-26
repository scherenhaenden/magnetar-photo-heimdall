namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Durable receipt returned for every submitted OperationRequest.
/// Re-submitting the same IdempotencyKey returns the original receipt.
/// </summary>
public sealed record OperationReceipt(
    string ReceiptId,
    string IdempotencyKey,
    OperationStatus Status,
    DateTimeOffset AcceptedAt,
    int EffectiveConcurrency,
    string? Reason = null);