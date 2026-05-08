using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries under <c>operations.manifestGroups</c>. Returns the 1-hop cross-group
/// dependency neighborhood used by the dashboard's DAG visualisation. Thin wrapper around
/// <see cref="IOperationsService.GetManifestGroupDependencyGraphAsync"/> so both surfaces
/// see exactly the same nodes and edges for a given group.
/// </summary>
public class ManifestGroupQueries
{
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
