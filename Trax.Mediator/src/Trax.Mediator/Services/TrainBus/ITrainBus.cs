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
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own. See the remarks.
    /// </param>
    /// <returns>A task that resolves to the train's output.</returns>
    /// <remarks>
    /// This method dynamically discovers and executes the appropriate train based on the
    /// type of the input object. The train must be registered with the train registry
    /// and must return the specified output type.
    ///
    /// If metadata is provided, the train runs as that metadata instead of creating its own: it
    /// must be a pre-created record in the <c>Pending</c> state, as the scheduler and the
    /// dashboard's ad-hoc run create, and anything else is refused with a TrainException. It
    /// does not make the new run a child of another; passing a running train's metadata throws.
    ///
    /// If no registered train takes the input's type, a
    /// <see cref="Exceptions.NoTrainForInputException"/> is thrown. Its message describes how the
    /// host is built and is for the host's log, not for a caller.
    /// </remarks>
    public Task<TOut> RunAsync<TOut>(object trainInput, Metadata? metadata = null);

    /// <summary>
    /// Executes a train with cancellation support.
    /// </summary>
    /// <typeparam name="TOut">The expected output type of the train.</typeparam>
    /// <param name="trainInput">The input object for the train.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own. It is not a parent link: any other state, including a running train's
    /// metadata, is refused with a TrainException. See <see cref="RunAsync{TOut}(object, Metadata?)"/>.
    /// </param>
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
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own. It is not a parent link: any other state, including a running train's
    /// metadata, is refused with a TrainException. See <see cref="RunAsync{TOut}(object, Metadata?)"/>.
    /// </param>
    public Task RunAsync(object trainInput, Metadata? metadata = null);

    /// <summary>
    /// Executes a train with cancellation support, discarding the output.
    /// </summary>
    /// <param name="trainInput">The input object for the train.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own. It is not a parent link: any other state, including a running train's
    /// metadata, is refused with a TrainException. See <see cref="RunAsync{TOut}(object, Metadata?)"/>.
    /// </param>
    public Task RunAsync(
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    );

    /// <summary>
    /// Runs the train registered under <paramref name="trainName"/> and returns its output. Unlike
    /// the input-keyed <c>RunAsync</c>, this runs that train even when another train takes the
    /// same input type. Creates a child DI scope for the run and disposes it afterwards.
    /// </summary>
    /// <typeparam name="TOut">The train's output type.</typeparam>
    /// <param name="trainName">
    /// The full name of the train's service type, as <c>TrainRegistration.ServiceType.FullName</c>
    /// and a run's metadata name record it.
    /// </param>
    /// <param name="trainInput">The input; it must be of the train's input type.</param>
    /// <param name="cancellationToken">Passed to the train's <c>Run</c>.</param>
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own. See <see cref="RunAsync{TOut}(object, Metadata?)"/>.
    /// </param>
    /// <returns>A task that resolves to the train's output.</returns>
    /// <remarks>
    /// Like the rest of the bus this is an in-process call and checks no authorization:
    /// <c>ITrainExecutionService.RunAsync</c> authorizes a caller and then runs the train it
    /// authorized through this method. The default implementation throws
    /// <see cref="NotSupportedException"/>; the bus <c>AddMediator</c> registers implements it.
    /// </remarks>
    /// <exception cref="Trax.Core.Exceptions.TrainException">
    /// No train is registered under <paramref name="trainName"/>, the input is not of its input
    /// type, or <paramref name="metadata"/> is not <c>Pending</c>.
    /// </exception>
    public Task<TOut> RunByNameAsync<TOut>(
        string trainName,
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    ) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not implement {nameof(RunByNameAsync)}."
        );

    /// <summary>
    /// Runs the train registered under <paramref name="trainName"/> and discards its output. See
    /// <see cref="RunByNameAsync{TOut}(string, object, CancellationToken, Metadata?)"/>.
    /// </summary>
    /// <param name="trainName">The full name of the train's service type.</param>
    /// <param name="trainInput">The input; it must be of the train's input type.</param>
    /// <param name="cancellationToken">Passed to the train's <c>Run</c>.</param>
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own.
    /// </param>
    /// <exception cref="Trax.Core.Exceptions.TrainException">
    /// No train is registered under <paramref name="trainName"/>, the input is not of its input
    /// type, or <paramref name="metadata"/> is not <c>Pending</c>.
    /// </exception>
    public Task RunByNameAsync(
        string trainName,
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    ) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not implement {nameof(RunByNameAsync)}."
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
