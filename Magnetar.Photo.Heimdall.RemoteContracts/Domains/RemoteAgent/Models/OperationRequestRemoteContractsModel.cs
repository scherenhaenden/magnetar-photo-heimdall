namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// A typed, idempotent work request.
/// IdempotencyKey is a client-generated opaque string (up to 128 chars)
/// that the agent uses to detect duplicates and return the original receipt.
/// RelativePath must be relative, forward-slash-separated, and free
/// of absolute markers, backslashes, null bytes, empty segments, '.' and '..'.
/// An empty path is the explicit configured-root selector for read-only
/// Scan/Hash/DuplicateScan kinds; the agent never interprets it as the process cwd.
/// </summary>
public sealed record OperationRequestRemoteContractsModel(
    ProtocolVersionRemoteContractsModel Protocol,
    string RootId,
    string RelativePath,
    OperationKindRemoteContractsModel Kind,
    int RequestedConcurrency,
    string IdempotencyKey,
    IReadOnlyList<string>? SourceRootIds = null,
    OrganizerProfileRemoteContractsModel? OrganizerProfile = null,
    string? PlanId = null,
    IReadOnlyList<string>? SelectedPlanItemIds = null)
{
    /// <summary>
    /// Validates the request against the agent's published capabilities.
    /// Throws RemoteContractsValidationException on any violation.
    /// </summary>
    public void ValidateAgainst(AgentCapabilitiesRemoteContractsModel capabilities)
    {
        Protocol.EnsureSupportedBy(capabilities.Protocol);

        if (!capabilities.AllowedRoots.Any(r => r.Id == RootId))
            throw new RemoteContractsValidationException(
                $"Root '{RootId}' is not published by this agent.");

        RemotePathRemoteContractsValidator.ValidateRelative(RelativePath, allowConfiguredRoot: Kind is
            OperationKindRemoteContractsModel.Scan or OperationKindRemoteContractsModel.Hash or OperationKindRemoteContractsModel.DuplicateScan or
            OperationKindRemoteContractsModel.ConsolidationPlan or OperationKindRemoteContractsModel.ConsolidationCopy or OperationKindRemoteContractsModel.ConsolidationQuarantine);

        if (!capabilities.AllowedOperations.Contains(Kind))
            throw new RemoteContractsValidationException(
                $"Operation kind '{Kind}' is not allowed by this agent.");

        if (Kind is OperationKindRemoteContractsModel.ConsolidationPlan or OperationKindRemoteContractsModel.ConsolidationCopy or OperationKindRemoteContractsModel.ConsolidationQuarantine)
        {
            if (SourceRootIds is null || SourceRootIds.Count == 0 || SourceRootIds.Any(string.IsNullOrWhiteSpace))
                throw new RemoteContractsValidationException("Consolidation requires one or more source root ids.");
            if (SourceRootIds.Distinct(StringComparer.Ordinal).Count() != SourceRootIds.Count || SourceRootIds.Contains(RootId, StringComparer.Ordinal))
                throw new RemoteContractsValidationException("Consolidation source roots must be distinct from each other and the destination root.");
            var allowedRootIds = capabilities.AllowedRoots.Select(root => root.Id).ToHashSet(StringComparer.Ordinal);
            if (SourceRootIds.Any(id => !allowedRootIds.Contains(id)))
                throw new RemoteContractsValidationException("Every consolidation source root must be published by this agent.");
            if (OrganizerProfile is null)
                throw new RemoteContractsValidationException("Consolidation requires the ordering profile selected for the destination library.");
            OrganizerProfile.Validate();
            if (Kind == OperationKindRemoteContractsModel.ConsolidationPlan)
            {
                if (PlanId is not null || SelectedPlanItemIds is not null)
                    throw new RemoteContractsValidationException("A preview cannot reference a previous plan or selected plan items.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(PlanId) || PlanId.Length > 128)
                    throw new RemoteContractsValidationException("Copy and quarantine require a valid PlanId from a preview.");
                if (SelectedPlanItemIds is null || SelectedPlanItemIds.Count == 0 || SelectedPlanItemIds.Any(string.IsNullOrWhiteSpace) ||
                    SelectedPlanItemIds.Distinct(StringComparer.Ordinal).Count() != SelectedPlanItemIds.Count)
                    throw new RemoteContractsValidationException("Copy and quarantine require distinct selected plan item ids.");
            }
        }

        if (RequestedConcurrency is < 1 || RequestedConcurrency > capabilities.MaximumConcurrency)
            throw new RemoteContractsValidationException(
                $"Requested concurrency {RequestedConcurrency} is outside the allowed range [1, {capabilities.MaximumConcurrency}].");

        if (string.IsNullOrWhiteSpace(IdempotencyKey) || IdempotencyKey.Length > 128)
            throw new RemoteContractsValidationException(
                "IdempotencyKey must be non-empty and no longer than 128 characters.");
    }
}
