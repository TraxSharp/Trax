namespace Trax.Scheduler.Services.Operations;

/// <param name="FromId">Parent group ID (the group whose manifests are depended on).</param>
/// <param name="ToId">Dependent group ID (the group whose manifests depend on the parent).</param>
public record DependencyGraphEdge(long FromId, long ToId);
