namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Optional knobs passed to
/// <see cref="IPersistedOperationStore.UpsertAsync"/>.
/// </summary>
public sealed record UpsertOptions
{
    /// <summary>
    /// Tenant scope. Null targets the single-tenant row set.
    /// </summary>
    public string? TenantKey { get; init; }

    /// <summary>
    /// Operator-facing description recorded on the row.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// When true, skips the shape-diff guardrail. The guardrail compares
    /// the proposed document's shape fingerprint against the existing row's
    /// fingerprint and throws <see cref="ShapeDiffViolationException"/> when
    /// they differ. Set this only when the operator has verified that the
    /// shape change will not break shipped clients (or when the change is
    /// intentionally a breaking version bump).
    /// </summary>
    public bool BypassShapeDiff { get; init; }

    /// <summary>
    /// Operator-controlled metadata. Stored on the row and surfaced on the
    /// dashboard for lifecycle tracking. Not used for request routing — the
    /// id is the contract with shipped clients. Defaults to <c>0</c>; bump
    /// it explicitly when shipping a new client that requests a new id.
    /// </summary>
    public int Version { get; init; }
}
