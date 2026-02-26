namespace Trax.Scheduler.Workflows.TaskServerExecutor;

/// <summary>
/// Wraps the resolved workflow input for type-safe routing through Trax.Core's Memory system.
/// </summary>
/// <remarks>
/// Trax.Core stores tuple elements by runtime type but reconstructs tuples by declared generic type.
/// Using raw <c>object</c> as a tuple element won't resolve from Memory. This wrapper provides
/// a concrete type for Memory routing when passing workflow input through the TaskServerExecutor chain.
/// </remarks>
/// <param name="Value">The workflow input object</param>
public record ResolvedWorkflowInput(object Value);
