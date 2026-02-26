using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;

namespace Trax.Dashboard.Utilities;

public static class AdminQueryFilters
{
    public static IQueryable<Metadata> ExcludeAdmin(
        this IQueryable<Metadata> query,
        IReadOnlyList<string> adminNames
    )
    {
        foreach (var name in adminNames)
            query = query.Where(m => !m.Name.EndsWith(name));
        return query;
    }

    public static IQueryable<Manifest> ExcludeAdmin(
        this IQueryable<Manifest> query,
        IReadOnlyList<string> adminNames
    )
    {
        foreach (var name in adminNames)
            query = query.Where(m => !m.Name.EndsWith(name));
        return query;
    }
}
