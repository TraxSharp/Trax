namespace Trax.Api.DTOs;

/// <summary>
/// Paginated result set with optional keyset cursor and count estimation metadata.
/// </summary>
/// <param name="Items">The items for this page</param>
/// <param name="TotalCount">Total matching items (may be an estimate for large tables)</param>
/// <param name="Skip">Offset used (0 when cursor-based pagination is used)</param>
/// <param name="Take">Page size requested</param>
/// <param name="IsEstimatedCount">True when TotalCount is a statistical estimate from pg_class.reltuples</param>
/// <param name="NextCursor">The Id of the last item in this page, for keyset pagination</param>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Skip,
    int Take,
    bool IsEstimatedCount = false,
    long? NextCursor = null
);
