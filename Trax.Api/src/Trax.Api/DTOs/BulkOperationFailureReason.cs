using Trax.Api.Services.BulkOperations;

namespace Trax.Api.DTOs;

/// <summary>One reason a bulk operation's ids did not take, and how many.</summary>
/// <param name="Reason">The reason.</param>
/// <param name="Count">How many ids failed for it.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record BulkOperationFailureReason(string Reason, int Count);
