using Trax.Mediator.Services.WorkflowRegistry;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for <see cref="IWorkflowRegistry"/> used by the scheduler.
/// </summary>
public static class WorkflowRegistryExtensions
{
    /// <summary>
    /// Validates that a workflow is registered for the specified input type.
    /// </summary>
    /// <typeparam name="TInput">The input type to validate</typeparam>
    /// <param name="registry">The workflow registry to check</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no workflow is registered for the input type.
    /// </exception>
    public static void ValidateWorkflowRegistration<TInput>(this IWorkflowRegistry registry) =>
        registry.ValidateWorkflowRegistration(typeof(TInput));

    internal static void ValidateWorkflowRegistration(
        this IWorkflowRegistry registry,
        Type inputType
    )
    {
        if (!registry.InputTypeToWorkflow.ContainsKey(inputType))
        {
            throw new InvalidOperationException(
                $"Workflow for input type '{inputType.Name}' is not registered in the WorkflowRegistry. "
                    + $"Ensure the workflow assembly is included in AddEffectWorkflowBus()."
            );
        }
    }
}
