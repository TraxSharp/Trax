using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// The filter arguments of <c>operations.deadLetters.deadLetters</c>, without its paging.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record BulkDeadLetterFilter
{
    /// <summary>Only dead letters in this status.</summary>
    public DeadLetterStatus? Status { get; init; }

    /// <summary>Only dead letters of this manifest.</summary>
    public long? ManifestId { get; init; }
}
