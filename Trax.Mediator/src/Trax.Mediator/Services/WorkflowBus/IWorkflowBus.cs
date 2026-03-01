using Trax.Core.Route;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Services.WorkflowBus;

/// <summary>
/// Defines a workflow bus that can dynamically execute workflows based on their input type.
/// </summary>
/// <remarks>
/// The workflow bus acts as a mediator between the application and workflow implementations.
/// It allows for dynamic discovery and execution of workflows without requiring direct references
/// to specific workflow implementations. This promotes loose coupling and enables a more
/// flexible architecture where workflows can be added or modified without changing the code
/// that executes them.
///
/// The workflow bus uses a registry of workflows indexed by their input types to determine
/// which workflow to execute for a given input. This enables a type-based dispatch mechanism
/// where the appropriate workflow is selected automatically based on the type of the input.
///
/// Example usage:
/// ```csharp
/// // Inject the workflow bus
/// public class MyService(IWorkflowBus workflowBus)
/// {
///     public async Task ProcessOrder(OrderInput input)
///     {
///         // The bus will automatically find and execute the workflow that handles OrderInput
///         var result = await workflowBus.RunAsync<OrderResult>(input);
///         // Process the result
///     }
/// }
/// ```
/// </remarks>
public interface IWorkflowBus
{
    /// <summary>
    /// Executes a workflow that accepts the specified input type and returns the specified output type.
    /// </summary>
    /// <typeparam name="TOut">The expected output type of the workflow</typeparam>
    /// <param name="workflowInput">The input object for the workflow</param>
    /// <param name="metadata">Optional metadata to associate with the workflow execution</param>
    /// <returns>A task that resolves to the workflow's output</returns>
    /// <remarks>
    /// This method dynamically discovers and executes the appropriate workflow based on the
    /// type of the input object. The workflow must be registered with the workflow registry
    /// and must return the specified output type.
    ///
    /// If metadata is provided, it will be associated with the workflow execution, which can
    /// be useful for tracking, logging, and debugging purposes. The metadata's ID will be set
    /// as the ParentId of the workflow, establishing a parent-child relationship between
    /// workflow executions.
    ///
    /// If no workflow is found that can handle the specified input type, a WorkflowException
    /// will be thrown.
    /// </remarks>
    public Task<TOut> RunAsync<TOut>(object workflowInput, Metadata? metadata = null);

    /// <summary>
    /// Executes a workflow with cancellation support.
    /// </summary>
    /// <typeparam name="TOut">The expected output type of the workflow</typeparam>
    /// <param name="workflowInput">The input object for the workflow</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <param name="metadata">Optional metadata to associate with the workflow execution</param>
    /// <returns>A task that resolves to the workflow's output</returns>
    public Task<TOut> RunAsync<TOut>(
        object workflowInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    );

    public Task RunAsync(object workflowInput, Metadata? metadata = null);

    /// <summary>
    /// Executes a workflow with cancellation support, returning Unit.
    /// </summary>
    /// <param name="workflowInput">The input object for the workflow</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <param name="metadata">Optional metadata to associate with the workflow execution</param>
    public Task RunAsync(
        object workflowInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    );

    public object InitializeWorkflow(object workflowInput);
}
