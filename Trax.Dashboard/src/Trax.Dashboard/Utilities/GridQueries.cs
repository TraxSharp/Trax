using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Configuration;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// The base queries of the dashboard's busiest grids: their scope and default order, before the
/// grid's filter, sort and page. Kept here rather than inline in each page so the stress suite
/// times the query the page runs.
/// </summary>
internal static class GridQueries
{
    /// <summary>The runs list: every run, newest first, without admin trains when hidden.</summary>
    public static IQueryable<Metadata> Runs(IDataContext db, bool hideAdminTrains)
    {
        IQueryable<Metadata> query = db.Metadatas.AsNoTracking();

        if (hideAdminTrains)
            query = query.ExcludeAdmin(AdminTrains.FullNames);

        return query.OrderByDescending(m => m.Id);
    }

    /// <summary>A manifest group's runs, most recently started first.</summary>
    public static IQueryable<Metadata> RunsOfGroup(IDataContext db, long manifestGroupId)
    {
        // Subquery — generates SQL subselect, not a materialized IN list.
        // No AsNoTracking — this is composed into the outer query, never materialized.
        var manifestIdsSubquery = db
            .Manifests.Where(m => m.ManifestGroupId == manifestGroupId)
            .Select(m => m.Id);

        return db
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId.HasValue && manifestIdsSubquery.Contains(m.ManifestId.Value))
            .OrderByDescending(m => m.StartTime);
    }

    /// <summary>The work queue list: every entry, newest first.</summary>
    public static IQueryable<WorkQueue> WorkQueue(IDataContext db) =>
        db.WorkQueues.AsNoTracking().OrderByDescending(q => q.Id);

    /// <summary>The manifests list: every manifest, newest first, without admin trains when hidden.</summary>
    public static IQueryable<Manifest> Manifests(IDataContext db, bool hideAdminTrains)
    {
        IQueryable<Manifest> query = db.Manifests.AsNoTracking();

        if (hideAdminTrains)
            query = query.ExcludeAdmin(AdminTrains.FullNames);

        return query.OrderByDescending(m => m.Id);
    }
}
