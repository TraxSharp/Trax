using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries under <c>operations.manifestGroups</c>. Holds the paginated list of manifest
/// groups, single-group lookup, and the 1-hop cross-group dependency neighborhood used
/// by the dashboard's DAG visualisation. The list and single-group reads hit the
/// <c>manifest_group</c> table directly via EF; <c>graph</c> delegates to
/// <see cref="IOperationsService"/> so the GraphQL surface and the in-process dashboard
/// see identical nodes and edges.
/// </summary>
public class ManifestGroupQueries
{
    /// <summary>
    /// Paginated list of manifest groups, newest first. Lives here (not on
    /// <see cref="OperationsQueries"/>) because the parent's <c>manifestGroups</c> field
    /// returns this namespace; a sibling field with the same camelCased name would
    /// collide and HotChocolate would silently drop one.
    /// </summary>
    public async Task<PagedResult<ManifestGroupSummary>> GetGroups(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        string? nameContains = null,
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        IQueryable<Effect.Models.ManifestGroup.ManifestGroup> baseQuery = db
            .ManifestGroups.AsNoTracking()
            .OrderByDescending(g => g.Id);

        if (!string.IsNullOrWhiteSpace(nameContains))
            baseQuery = baseQuery.Where(g => g.Name.Contains(nameContains));

        var hasFilter = !string.IsNullOrWhiteSpace(nameContains);

        var (totalCount, isEstimate) =
            (afterId.HasValue || hasFilter)
                ? (await baseQuery.CountAsync(ct), false)
                : await CountEstimator.EstimateOrCountAsync(
                    db,
                    "manifest_group",
                    () => baseQuery.CountAsync(ct),
                    ct
                );

        var query = afterId.HasValue ? baseQuery.Where(g => g.Id < afterId.Value) : baseQuery;

        if (!afterId.HasValue && skip > 0)
            query = query.Skip(skip);

        var items = await query
            .Take(take)
            .Select(g => new ManifestGroupSummary(
                g.Id,
                g.Name,
                g.MaxActiveJobs,
                g.Priority,
                g.IsEnabled,
                g.CreatedAt,
                g.UpdatedAt
            ))
            .ToListAsync(ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new PagedResult<ManifestGroupSummary>(
            items,
            totalCount,
            afterId.HasValue ? 0 : skip,
            take,
            isEstimate,
            nextCursor
        );
    }

    /// <summary>
    /// Single-group lookup by id. Returns <c>null</c> when the group does not exist.
    /// Required by the dashboard's group detail page so it can pre-populate the
    /// <c>maxActiveJobs</c>, <c>priority</c>, and <c>isEnabled</c> form before the
    /// operator submits a patch.
    /// </summary>
    public async Task<ManifestGroupSummary?> GetGroup(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        return await db
            .ManifestGroups.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new ManifestGroupSummary(
                g.Id,
                g.Name,
                g.MaxActiveJobs,
                g.Priority,
                g.IsEnabled,
                g.CreatedAt,
                g.UpdatedAt
            ))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Execution roll-up for a set of manifest groups: manifest count, executions by state, and
    /// last run per group. Batched so the dashboard's groups list fetches stats for just the
    /// visible page in one round-trip. Every requested id gets a row (zeros when it has no
    /// manifests or executions), in the order requested, so the caller can zip it to its rows.
    /// The metadata side is served by ix_metadata_manifest_state, the manifest side by
    /// ix_manifest_manifest_group_id.
    /// </summary>
    public async Task<IReadOnlyList<ManifestGroupStats>> GetStats(
        long[] groupIds,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        var ids = groupIds.Distinct().ToArray();
        if (ids.Length == 0)
            return Array.Empty<ManifestGroupStats>();

        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var manifestCounts = await db
            .Manifests.AsNoTracking()
            .Where(m => ids.Contains(m.ManifestGroupId))
            .GroupBy(m => m.ManifestGroupId)
            .Select(g => new { GroupId = g.Key, Count = (long)g.Count() })
            .ToListAsync(ct);

        // Join metadata to the group's manifests, then aggregate per (group, state). The join
        // stays cheap: the manifest side is filtered to the requested groups first, and each
        // manifest_id seek hits ix_metadata_manifest_state.
        var execAgg = await db
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId != null)
            .Join(
                db.Manifests.AsNoTracking().Where(mf => ids.Contains(mf.ManifestGroupId)),
                m => m.ManifestId,
                mf => (long?)mf.Id,
                (m, mf) =>
                    new
                    {
                        mf.ManifestGroupId,
                        m.TrainState,
                        m.StartTime,
                    }
            )
            .GroupBy(x => new { x.ManifestGroupId, x.TrainState })
            .Select(g => new
            {
                g.Key.ManifestGroupId,
                g.Key.TrainState,
                Count = (long)g.Count(),
                LastRun = g.Max(x => (DateTime?)x.StartTime),
            })
            .ToListAsync(ct);

        return ids.Select(id =>
            {
                var manifestCount = manifestCounts.FirstOrDefault(x => x.GroupId == id)?.Count ?? 0;
                var rows = execAgg.Where(x => x.ManifestGroupId == id).ToList();
                long StateCount(TrainState state) =>
                    rows.Where(x => x.TrainState == state).Sum(x => x.Count);
                var lastRun = rows.Count == 0 ? (DateTime?)null : rows.Max(x => x.LastRun);
                return new ManifestGroupStats(
                    id,
                    ManifestCount: manifestCount,
                    TotalExecutions: rows.Sum(x => x.Count),
                    Completed: StateCount(TrainState.Completed),
                    Failed: StateCount(TrainState.Failed),
                    InProgress: StateCount(TrainState.InProgress),
                    LastRun: lastRun
                );
            })
            .ToList();
    }

    /// <summary>
    /// Returns the dependency graph for the given group, or <c>null</c> if the group
    /// does not exist. Empty groups still return a single-node graph (the focal group)
    /// so the UI can render it.
    /// </summary>
    public async Task<ManifestGroupDependencyGraph?> GetGraph(
        long groupId,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        return await operationsService.GetManifestGroupDependencyGraphAsync(groupId, ct);
    }

    /// <summary>
    /// The whole cross-group dependency graph: every group as a node and every cross-group
    /// dependency as a directed parent → dependent edge. Nothing is highlighted. Backs the
    /// dashboard's global dependency graph on the manifest-groups page.
    /// </summary>
    public async Task<ManifestGroupDependencyGraph> GetDependencyGraph(
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        return await operationsService.GetGlobalManifestGroupGraphAsync(ct);
    }
}
