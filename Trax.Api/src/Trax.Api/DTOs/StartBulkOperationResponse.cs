using Trax.Api.Services.BulkOperations;

namespace Trax.Api.DTOs;

/// <summary>
/// The answer to <c>startBulkOperation</c>. A refusal starts nothing: <c>success</c> is
/// <see langword="false"/> and <c>message</c> says why. When the filter matched a different number of
/// ids than the caller confirmed, <c>matched</c> is the number it matches now, to confirm
/// and start again. While another bulk operation runs on this node, <c>operation</c> is
/// that one.
/// </summary>
/// <param name="Success">Whether an operation was started.</param>
/// <param name="Message">What happened, for an operator.</param>
/// <param name="Matched">How many ids the filter matched, or <see langword="null"/> when it was not read.</param>
/// <param name="Operation">The started operation, or the one already running; else <see langword="null"/>.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public sealed record StartBulkOperationResponse(
    bool Success,
    string Message,
    int? Matched,
    BulkOperation? Operation
);
