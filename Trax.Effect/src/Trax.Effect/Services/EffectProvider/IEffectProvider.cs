using Trax.Effect.Models;

namespace Trax.Effect.Services.EffectProvider;

/// <summary>
/// Defines the contract for a component that tracks and persists train metadata.
/// </summary>
/// <remarks>
/// The IEffectProvider interface is a key abstraction in the Trax.Effect system.
/// It represents a mechanism for tracking and persisting train metadata, such as
/// execution details, inputs, outputs, and error information.
///
/// Different implementations of this interface can store metadata in different ways,
/// such as in a database, in memory, or in a file. This allows for flexibility in
/// how train execution is tracked and monitored.
///
/// The provider pattern used here allows for:
/// 1. Separation of concerns between train execution and metadata tracking
/// 2. Multiple tracking mechanisms to be used simultaneously
/// 3. Easy extension with new tracking mechanisms
/// </remarks>
public interface IEffectProvider : IDisposable
{
    /// <summary>
    /// Persists any pending changes to the underlying storage mechanism.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method should ensure that all tracked models are persisted to the
    /// provider's storage mechanism. The actual implementation depends on the
    /// specific provider, but typically involves writing to a database, file,
    /// or other persistent storage.
    /// </remarks>
    Task SaveChanges(CancellationToken cancellationToken);

    /// <summary>
    /// Registers a NEW model to be tracked by this provider.
    /// </summary>
    /// <param name="model">The model to track</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method should prepare the model for persistence, but not necessarily
    /// persist it immediately. The actual persistence typically occurs when
    /// SaveChanges is called.
    ///
    /// Models typically represent train metadata, such as execution details,
    /// inputs, outputs, and error information.
    /// </remarks>
    Task Track(IModel model);

    /// <summary>
    /// Notifies this provider that a previously tracked model has been mutated.
    /// </summary>
    /// <param name="model">The model that was updated</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method is called after a tracked model's properties have been changed.
    /// Providers can use this notification to react to changes, such as re-serializing
    /// parameters or snapshotting state. The model passed here should have been
    /// previously registered via the Track method.
    ///
    /// The typical pattern is mutate-then-notify: the caller modifies the model's
    /// properties directly, then calls Update to notify all providers of the change.
    /// </remarks>
    Task Update(IModel model);
}
