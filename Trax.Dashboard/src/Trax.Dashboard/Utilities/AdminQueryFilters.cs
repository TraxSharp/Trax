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
/// </summary>
public static class AdminQueryFilters
{
    public static IQueryable<Metadata> ExcludeAdmin(
        this IQueryable<Metadata> query,
        IReadOnlyList<string> adminNames
    )
    {
        var names = adminNames.ToList();
        return names.Count == 0 ? query : query.Where(m => !names.Contains(m.Name));
    }

    public static IQueryable<Manifest> ExcludeAdmin(
        this IQueryable<Manifest> query,
        IReadOnlyList<string> adminNames
    )
    {
        var names = adminNames.ToList();
        return names.Count == 0 ? query : query.Where(m => !names.Contains(m.Name));
    }
}
