namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// 1-hop neighborhood of cross-group dependencies for a manifest group.
/// Returned by <see cref="IOperationsService.GetManifestGroupDependencyGraphAsync"/> and
/// consumed by both the GraphQL <c>operations.manifestGroups.graph</c> query and the
/// dashboard's dependency DAG visualisation.
/// </summary>
/// <param name="Nodes">All groups in the neighborhood plus the focal group itself.</param>
/// <param name="Edges">Directed edges between groups (parent → dependent).</param>
public record ManifestGroupDependencyGraph(
    IReadOnlyList<DependencyGraphNode> Nodes,
    IReadOnlyList<DependencyGraphEdge> Edges
);
