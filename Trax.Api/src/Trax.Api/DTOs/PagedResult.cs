namespace Trax.Api.DTOs;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Skip, int Take);
