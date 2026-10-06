using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// Hides the scheduler's administrative trains from a query. A row is hidden when its name is
/// one of the admin names passed in exactly, which the pages pass as
/// <c>AdminTrains.FullNames</c>: a train's stored name is its interface FullName, and that is
/// the list and the comparison the API's <c>hideAdminTrains</c> filter uses, so both surfaces
/// hide the same rows. A suffix match on the short names also hid a consumer's own train whose
/// name happened to end in one of them.
///
/// Infrastructure for the dashboard's own list pages; not intended to be called directly.
/// </summary>
internal static class AdminQueryFilters
{
    /// <summary>
    /// Filters out metadata rows (runs) whose <c>Name</c> equals one of <paramref name="adminNames"/>.
    /// Composes into the query, so the filter runs in the database.
    /// </summary>
    /// <param name="query">The runs to filter.</param>
    /// <param name="adminNames">Exact train names to exclude, normally <c>AdminTrains.FullNames</c>. Empty returns <paramref name="query"/> unchanged.</param>
    /// <returns>The filtered query.</returns>
    public static IQueryable<Metadata> ExcludeAdmin(
        this IQueryable<Metadata> query,
        IReadOnlyList<string> adminNames
    )
    {
        var names = adminNames.ToList();
        return names.Count == 0 ? query : query.Where(m => !names.Contains(m.Name));
    }

    /// <summary>
    /// Filters out manifests whose <c>Name</c> equals one of <paramref name="adminNames"/>.
    /// Composes into the query, so the filter runs in the database.
    /// </summary>
    /// <param name="query">The manifests to filter.</param>
    /// <param name="adminNames">Exact train names to exclude, normally <c>AdminTrains.FullNames</c>. Empty returns <paramref name="query"/> unchanged.</param>
    /// <returns>The filtered query.</returns>
    public static IQueryable<Manifest> ExcludeAdmin(
        this IQueryable<Manifest> query,
        IReadOnlyList<string> adminNames
    )
    {
        var names = adminNames.ToList();
        return names.Count == 0 ? query : query.Where(m => !names.Contains(m.Name));
    }
}
