using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
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
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var baseQuery = db.ManifestGroups.AsNoTracking().OrderByDescending(g => g.Id);

        var (totalCount, isEstimate) = afterId.HasValue
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
}
