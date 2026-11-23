using Trax.Api.Services.BulkOperations;

namespace Trax.Api.DTOs;

/// <summary>One id a bulk operation did not apply to, and why.</summary>
/// <param name="Id">The run, dead letter or manifest id.</param>
/// <param name="Reason">Why it did not take, in the operator's words.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record BulkOperationFailure(long Id, string Reason);
