using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Models;

namespace Trax.Effect.Provider.Json.Services.JsonEffect;

/// <summary>
/// Implements a JSON-based effect provider for tracking and serializing model changes.
/// </summary>
/// <remarks>
/// The JsonEffectProvider class provides an implementation of the IJsonEffectProvider interface
/// that uses JSON serialization to track and record changes to models.
///
/// This provider maintains a dictionary of tracked models and their previous serialized states.
/// When changes are saved, it compares the current serialized state of each model with its
/// previous state, and logs any changes that are detected.
///
/// The provider uses the JSON serialization options configured in the Trax.Core effect
/// configuration to serialize models to JSON format.
///
/// This implementation is useful for debugging, auditing, and logging purposes, as it
/// provides a way to track and record all changes made to models during train execution.
/// </remarks>
/// <param name="logger">The logger used to log model changes</param>
/// <param name="configuration">The Trax.Core effect configuration containing JSON serialization options</param>
public class JsonEffectProvider(
    ILogger<JsonEffectProvider> logger,
    ITraxEffectConfiguration configuration
) : IJsonEffectProvider
{
    private readonly Dictionary<IModel, string> _previousStates = new();
    private readonly HashSet<IModel> _trackedModels = [];
    private readonly object _lock = new();

    /// <summary>
    /// Disposes the effect provider and releases any resources.
    /// </summary>
    /// <remarks>
    /// This implementation clears all tracked models and their previous states
    /// to prevent memory leaks.
    /// </remarks>
    public void Dispose()
    {
        // Clear all tracked models to release references and prevent memory leaks
        _trackedModels.Clear();
        _previousStates.Clear();
    }

    /// <summary>
    /// Saves changes to tracked models by detecting and logging changes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Gets the JSON serialization options from the configuration
    /// 2. Iterates through all tracked models
    /// 3. Serializes each model to JSON format
    /// 4. Compares the current serialized state with the previous state
    /// 5. If the state has changed, updates the previous state and adds the model to the changed models list
    /// 6. Logs the serialized state of each changed model
    ///
    /// This allows for tracking and logging all changes made to models during train execution,
    /// which can be useful for debugging, auditing, and logging purposes.
    /// </remarks>
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        var options = configuration.SystemJsonSerializerOptions;
        var changedModels = new List<IModel>();

        lock (_lock)
        {
            foreach (var model in _trackedModels)
            {
                var currentState = JsonSerializer.Serialize(model, model.GetType(), options);

                if (
                    !_previousStates.TryGetValue(model, out var previousState)
                    || previousState != currentState
                )
                {
                    _previousStates[model] = currentState;
                    changedModels.Add(model);
                }
            }
        }

        // Log outside of lock to prevent holding lock during logging
        foreach (var model in changedModels)
        {
            lock (_lock)
            {
                if (!_previousStates.TryGetValue(model, out var state))
                    break;

                logger.Log(configuration.LogLevel, "{@Metadata}", model);
            }
        }
    }

    /// <summary>
    /// Begins tracking a model for changes.
    /// </summary>
    /// <param name="model">The model to track</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method adds the specified model to the set of tracked models and stores its
    /// initial serialized state. The model will be tracked until the effect provider is
    /// disposed or the model is explicitly removed from tracking.
    ///
    /// When a model is first tracked, its initial serialized state is stored as the
    /// baseline for future comparisons. This allows the SaveChanges method to detect
    /// and log any changes made to the model after it was first tracked.
    /// </remarks>
    public async Task Track(IModel model)
    {
        lock (_lock)
        {
            if (_trackedModels.Add(model))
            {
                // Store initial serialized state when tracking starts
                _previousStates[model] = JsonSerializer.Serialize(
                    model,
                    model.GetType(),
                    configuration.SystemJsonSerializerOptions
                );
            }
        }
    }

    /// <inheritdoc />
    public Task Update(IModel model) => Task.CompletedTask;
}
