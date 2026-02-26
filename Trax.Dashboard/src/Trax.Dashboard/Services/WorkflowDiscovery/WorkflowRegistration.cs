using Microsoft.Extensions.DependencyInjection;

namespace Trax.Dashboard.Services.WorkflowDiscovery;

/// <summary>
/// Represents a discovered IEffectWorkflow registration in the DI container.
/// </summary>
public class WorkflowRegistration
{
    public required Type ServiceType { get; init; }
    public required Type ImplementationType { get; init; }
    public required Type InputType { get; init; }
    public required Type OutputType { get; init; }
    public required ServiceLifetime Lifetime { get; init; }

    public required string ServiceTypeName { get; init; }
    public required string ImplementationTypeName { get; init; }
    public required string InputTypeName { get; init; }
    public required string OutputTypeName { get; init; }
}
