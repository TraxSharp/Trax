using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// The filter arguments of <c>operations.manifests</c>, without its paging.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record BulkManifestFilter
{
    /// <summary>Only enabled (<see langword="true"/>) or disabled (<see langword="false"/>) manifests.</summary>
    public bool? IsEnabled { get; init; }

    /// <summary>Only manifests with this schedule type.</summary>
    public ScheduleType? ScheduleType { get; init; }

    /// <summary>Only manifests whose train name contains this text.</summary>
    public string? NameContains { get; init; }

    /// <summary>Only manifests in this group.</summary>
    public long? ManifestGroupId { get; init; }

    /// <summary>Leave out the scheduler's own internal trains' manifests.</summary>
    public bool? HideAdminTrains { get; init; }
}
