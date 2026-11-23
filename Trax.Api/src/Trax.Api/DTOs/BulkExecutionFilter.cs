using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// The filter arguments of <c>operations.executions</c>, without its paging and order. An
/// omitted field filters nothing, as it does on the list. The list's total under a failure text
/// filter stops at 10,000; a bulk operation counts every match.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record BulkExecutionFilter
{
    /// <summary>Only runs in this state.</summary>
    public TrainState? TrainState { get; init; }

    /// <summary>Only runs of this train (the train interface's full name, matched exactly).</summary>
    public string? TrainName { get; init; }

    /// <summary>Only runs that started at or after this time (UTC).</summary>
    public DateTime? StartedAfter { get; init; }

    /// <summary>Only runs that started at or before this time (UTC).</summary>
    public DateTime? StartedBefore { get; init; }

    /// <summary>Only runs scheduled by this manifest.</summary>
    public long? ManifestId { get; init; }

    /// <summary>Only runs scheduled by a manifest in this group.</summary>
    public long? ManifestGroupId { get; init; }

    /// <summary>Leave out the scheduler's own internal trains.</summary>
    public bool? HideAdminTrains { get; init; }

    /// <summary>Only runs whose failure was classified this way.</summary>
    public FailureClass? FailureClass { get; init; }

    /// <summary>Only the run with this external id (matched exactly).</summary>
    public string? ExternalId { get; init; }

    /// <summary>Only runs started from inside this run.</summary>
    public long? ParentId { get; init; }

    /// <summary>Only runs run on the machine with this name (matched exactly).</summary>
    public string? HostName { get; init; }

    /// <summary>Only runs whose failure reason contains this text, ignoring case.</summary>
    public string? FailureReasonContains { get; init; }

    /// <summary>Only runs that failed in the junction with this name (matched exactly).</summary>
    public string? FailureJunction { get; init; }
}
