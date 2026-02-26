namespace Trax.Dashboard.Services.WorkflowDiscovery;

/// <summary>
/// Discovers all IEffectWorkflow registrations available in the DI container.
/// </summary>
public interface IWorkflowDiscoveryService
{
    IReadOnlyList<WorkflowRegistration> DiscoverWorkflows();
}
