using Trax.Api.Services.BulkOperations;

namespace Trax.Api.DTOs;

/// <summary>Where a bulk operation is.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public enum BulkOperationStatus
{
    /// <summary>Batches are still running.</summary>
    Running,

    /// <summary>
    /// Every id it matched was attempted. <c>failed</c> counts the ones that
    /// did not take, each with its reason.
    /// </summary>
    Finished,

    /// <summary>
    /// It stopped before the end: the server refused a batch (the message says why) or failed
    /// (the detail is in the server's log). The ids it had not reached were not attempted.
    /// </summary>
    Failed,

    /// <summary>The server was shutting down, so it stopped between batches.</summary>
    Canceled,
}
