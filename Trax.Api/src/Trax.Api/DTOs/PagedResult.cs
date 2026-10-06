namespace Trax.Api.DTOs;

/// <summary>
/// Paginated result set with optional keyset cursor and count estimation metadata.
/// </summary>
/// <param name="Items">The items for this page</param>
/// <param name="TotalCount">Total matching items (may be an estimate for large tables)</param>
/// <param name="Skip">Offset used (0 when cursor-based pagination is used)</param>
/// <param name="Take">Page size requested</param>
/// <param name="IsEstimatedCount">True when TotalCount is a statistical estimate rather than an exact count. Only the unfiltered total of a large list is estimated, on every page alike.</param>
/// <param name="NextCursor">The Id of the last item in this page, for keyset pagination</param>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Skip,
    int Take,
    bool IsEstimatedCount = false,
    long? NextCursor = null
)
{
    /// <summary>
    /// True when <see cref="TotalCount"/> stopped counting at its cap, so the list holds at least
    /// that many matches and probably more: read it as "10,000+". A capped count is a lower bound,
    /// not an estimate. Only the <c>logs</c> list filtered by text and the <c>executions</c> list
    /// filtered by failure text cap their count; every other list leaves this false.
    /// </summary>
    public bool IsCountCapped { get; init; }
}
