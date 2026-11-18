using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// What a bulk operation applies to: exactly one of its three lists, filtered the way that list's
/// read filters it. <c>startBulkOperation</c> refuses a filter that gives none or more than one,
/// and one the operation's kind does not apply to: <c>CANCEL</c> takes <c>executions</c>,
/// <c>REQUEUE</c> and <c>ACKNOWLEDGE</c> take <c>deadLetters</c>, and <c>TRIGGER</c>,
/// <c>ENABLE</c> and <c>DISABLE</c> take <c>manifests</c>. A bulk operation reads it back
/// as it was given.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record BulkOperationFilter
{
    /// <summary>Runs, filtered as <c>operations.executions</c> filters them.</summary>
    public BulkExecutionFilter? Executions { get; init; }

    /// <summary>Dead letters, filtered as <c>operations.deadLetters.deadLetters</c> filters them.</summary>
    public BulkDeadLetterFilter? DeadLetters { get; init; }

    /// <summary>Manifests, filtered as <c>operations.manifests</c> filters them.</summary>
    public BulkManifestFilter? Manifests { get; init; }
}
