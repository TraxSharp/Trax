using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;

/// <summary>
/// Implements an effect provider that serializes workflow input and output parameters to JSON format.
/// </summary>
/// <remarks>
/// The ParameterEffect class provides an implementation of the IEffectProvider interface
/// that serializes workflow input and output parameters to JSON format.
///
/// This provider tracks metadata objects and serializes their input and output parameters
/// to JSON format when changes are saved. The serialized parameters are stored in the
/// metadata object's Input and Output properties, which can then be persisted to a database
/// or other storage medium.
///
/// This implementation is useful for capturing and storing the input and output parameters
/// of workflow executions, which can be used for auditing, debugging, and analytics purposes.
/// </remarks>
/// <param name="options">The JSON serializer options to use for parameter serialization</param>
/// <param name="configuration">Runtime configuration controlling which parameters are serialized</param>
public class ParameterEffect(
    JsonSerializerOptions options,
    ParameterEffectConfiguration configuration
) : IEffectProvider
{
    private readonly HashSet<Metadata> _trackedMetadatas = [];
    private readonly object _lock = new();

    /// <summary>
    /// Saves changes to tracked metadata objects by serializing their input and output parameters.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method iterates through all tracked metadata objects and serializes their
    /// input and output parameters to JSON format. The serialized parameters are stored
    /// in the metadata object's Input and Output properties, which can then be persisted
    /// to a database or other storage medium.
    ///
    /// This allows for capturing and storing the input and output parameters of workflow
    /// executions, which can be used for auditing, debugging, and analytics purposes.
    /// </remarks>
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var metadata in _trackedMetadatas)
                SerializeParameters(metadata);
        }
    }

    /// <summary>
    /// Begins tracking a model for changes.
    /// </summary>
    /// <param name="model">The model to track</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method checks if the specified model is a Metadata object, and if so,
    /// adds it to the set of tracked metadata objects and serializes its input and
    /// output parameters.
    ///
    /// Only Metadata objects are tracked by this provider, as they are the only objects
    /// that contain input and output parameters that need to be serialized.
    ///
    /// When a metadata object is first tracked, its input and output parameters are
    /// immediately serialized to JSON format. This ensures that the parameters are
    /// captured even if the SaveChanges method is never called.
    /// </remarks>
    public async Task Track(IModel model)
    {
        if (model is Metadata metadata)
        {
            lock (_lock)
            {
                _trackedMetadatas.Add(metadata);
                SerializeParameters(metadata);
            }
        }
    }

    /// <inheritdoc />
    public Task Update(IModel model)
    {
        if (model is Metadata metadata)
        {
            lock (_lock)
            {
                if (_trackedMetadatas.Contains(metadata))
                    SerializeParameters(metadata);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Serializes the input and output parameters of a metadata object to JSON format.
    /// </summary>
    /// <param name="metadata">The metadata object whose parameters to serialize</param>
    /// <remarks>
    /// This method serializes the input and output parameters of the specified metadata
    /// object to JSON format. The serialized parameters are stored in the metadata object's
    /// Input and Output properties, which can then be persisted to a database or other
    /// storage medium.
    ///
    /// If the input or output parameter is null, it is not serialized. This prevents
    /// overwriting existing serialized parameters with null values.
    ///
    /// The serialization is performed using the JSON serializer options provided to the
    /// constructor, which allows for customizing the serialization process.
    ///
    /// IMPORTANT: This method queues existing JsonDocument instances for disposal after
    /// database operations complete, preventing memory leaks while avoiding disposed object issues.
    /// </remarks>
    private void SerializeParameters(Metadata metadata)
    {
        if (configuration.SaveInputs)
        {
            var inputObject = metadata.GetInputObject();
            if (inputObject is not null)
            {
                try
                {
                    metadata.Input = JsonSerializer.Serialize(inputObject, options);
                }
                catch (ObjectDisposedException)
                {
                    // Input object contains disposed JsonDocument, skip serialization
                    // This can happen when metadata contains disposed JsonDocument objects
                    metadata.Input ??=
                        """{"_disposed": true, "_message": "Input object contained disposed JsonDocument objects"}""";
                }
            }
        }

        if (configuration.SaveOutputs)
        {
            var outputObject = metadata.GetOutputObject();
            if (outputObject is not null)
            {
                try
                {
                    metadata.Output = JsonSerializer.Serialize(outputObject, options);
                }
                catch (ObjectDisposedException)
                {
                    // Output object contains disposed JsonDocument, skip serialization
                    // This can happen when metadata contains disposed JsonDocument objects
                    metadata.Output ??=
                        """{"_disposed": true, "_message": "Output object contained disposed JsonDocument objects"}""";
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var metadata in _trackedMetadatas)
            {
                metadata.SetInputObject(null);
                metadata.SetOutputObject(null);
            }
        }

        _trackedMetadatas.Clear();
    }
}
