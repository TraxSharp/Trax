namespace Trax.Api.DTOs;

/// <summary>
/// Ordering for paginated list reads. Both directions stay keyset-safe (they page on the
/// monotonic <c>id</c> index), so deep paging is O(page size) either way. Arbitrary-column
/// sorting is intentionally not offered: it is incompatible with keyset pagination at
/// millions of rows (it forces OFFSET scans or a full sort), which the stress suite guards
/// against. Filter to narrow the set instead.
/// </summary>
public enum SortOrder
{
    /// <summary>Newest first (id descending). The default.</summary>
    Newest,

    /// <summary>Oldest first (id ascending).</summary>
    Oldest,
}
