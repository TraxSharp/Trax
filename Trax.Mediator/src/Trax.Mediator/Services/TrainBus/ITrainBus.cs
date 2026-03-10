using System.ComponentModel;
using Trax.Core.Route;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Services.TrainBus;

/// <summary>
/// Defines a train bus that can dynamically execute trains based on their input type.
/// </summary>
/// <remarks>
/// The train bus acts as a mediator between the application and train implementations.
/// It allows for dynamic discovery and execution of trains without requiring direct references
/// to specific train implementations. This promotes loose coupling and enables a more
/// flexible architecture where trains can be added or modified without changing the code
/// that executes them.
///
/// The train bus uses a registry of trains indexed by their input types to determine
/// which train to execute for a given input. This enables a type-based dispatch mechanism
/// where the appropriate train is selected automatically based on the type of the input.
///
/// Example usage:
/// <code>
/// // Inject the train bus
/// public class MyService(ITrainBus trainBus)
/// {
///     public async Task ProcessOrder(OrderInput input)
///     {
///         // The bus will automatically find and execute the train that handles OrderInput
///         var result = await trainBus.RunAsync&lt;OrderResult&gt;(input);
///         // Process the result
///     }
/// }
/// </code>
/// </remarks>
public interface ITrainBus
{
    /// <summary>
    /// Executes a train that accepts the specified input type and returns the specified output type.
    /// </summary>
    /// <typeparam name="TOut">The expected output type of the train.</typeparam>
    /// <param name="trainInput">The input object for the train.</param>
    /// <param name="metadata">Optional metadata to associate with the train execution.</param>
    /// <returns>A task that resolves to the train's output.</returns>
    /// <remarks>
    /// This method dynamically discovers and executes the appropriate train based on the
    /// type of the input object. The train must be registered with the train registry
    /// and must return the specified output type.
    ///
    /// If metadata is provided, it will be associated with the train execution, which can
    /// be useful for tracking, logging, and debugging purposes. The metadata's ID will be set
    /// as the ParentId of the train, establishing a parent-child relationship between
    /// train executions.
    ///
    /// If no train is found that can handle the specified input type, a TrainException
    /// will be thrown.
    /// </remarks>
    public Task<TOut> RunAsync<TOut>(object trainInput, Metadata? metadata = null);

    /// <summary>
    /// Executes a train with cancellation support.
    /// </summary>
    /// <typeparam name="TOut">The expected output type of the train.</typeparam>
    /// <param name="trainInput">The input object for the train.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <param name="metadata">Optional metadata to associate with the train execution.</param>
    /// <returns>A task that resolves to the train's output.</returns>
    public Task<TOut> RunAsync<TOut>(
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    );

    /// <summary>
    /// Executes a train that accepts the specified input type, discarding the output.
    /// </summary>
    /// <param name="trainInput">The input object for the train.</param>
    /// <param name="metadata">Optional metadata to associate with the train execution.</param>
    public Task RunAsync(object trainInput, Metadata? metadata = null);

    /// <summary>
    /// Executes a train with cancellation support, discarding the output.
    /// </summary>
    /// <param name="trainInput">The input object for the train.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <param name="metadata">Optional metadata to associate with the train execution.</param>
    public Task RunAsync(
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    );

    /// <summary>
    /// Resolves and constructs a train instance for the given input type without executing it.
    /// Used internally by the scheduler and job runner.
    /// </summary>
    /// <param name="trainInput">The input object whose type determines which train to resolve.</param>
    /// <returns>The resolved train instance (unexecuted).</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object InitializeTrain(object trainInput);
}
