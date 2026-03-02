using Trax.Effect.Models;

namespace Trax.Effect.Services.EffectRunner;

/// <summary>
/// Defines the contract for a component that coordinates tracking and persistence
/// of train metadata across multiple effect providers.
/// </summary>
/// <remarks>
/// The IEffectRunner interface is a key abstraction in the Trax.Effect system.
/// It serves as a facade over multiple effect providers, allowing trains to interact
/// with a single component rather than managing multiple providers directly.
///
/// Implementations of this interface are responsible for:
/// 1. Managing the lifecycle of effect providers
/// 2. Delegating tracking operations to all registered providers
/// 3. Coordinating persistence operations across all providers
/// 4. Ensuring proper cleanup of resources
/// </remarks>
public interface IEffectRunner : IDisposable
{
    /// <summary>
    /// Persists any pending changes across all managed effect providers.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method should ensure that all tracked models are persisted to their
    /// respective storage mechanisms. It should handle coordination between
    /// multiple providers and manage any transactional behavior if required.
    /// </remarks>
    Task SaveChanges(CancellationToken cancellationToken);

    /// <summary>
    /// Registers a model to be tracked by all managed effect providers.
    /// </summary>
    /// <param name="model">The model to track</param>
    /// <remarks>
    /// This method should distribute the model to all registered providers
    /// for tracking. The actual persistence of the model will occur when
    /// SaveChanges is called.
    ///
    /// Models typically represent train metadata, such as execution details,
    /// inputs, outputs, and error information.
    /// </remarks>
    Task Track(IModel model);

    /// <summary>
    /// Notifies all managed effect providers that a previously tracked model has been mutated.
    /// </summary>
    /// <param name="model">The model that was updated</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method distributes the update notification to all registered providers,
    /// allowing each to react to the model mutation. This is called after the caller
    /// has directly modified the model's properties.
    /// </remarks>
    Task Update(IModel model);
}
