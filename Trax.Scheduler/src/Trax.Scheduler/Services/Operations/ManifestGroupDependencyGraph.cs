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

/// <param name="Id">Database ID of the manifest group.</param>
/// <param name="Name">Group name as stored on the row.</param>
/// <param name="IsHighlighted">True for the focal group; the UI uses this to render it differently.</param>
public record DependencyGraphNode(long Id, string Name, bool IsHighlighted);

/// <param name="FromId">Parent group ID (the group whose manifests are depended on).</param>
/// <param name="ToId">Dependent group ID (the group whose manifests depend on the parent).</param>
public record DependencyGraphEdge(long FromId, long ToId);
