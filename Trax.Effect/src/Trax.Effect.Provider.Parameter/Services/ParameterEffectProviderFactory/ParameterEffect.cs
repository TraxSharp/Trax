using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;

/// <summary>
/// Implements an effect provider that serializes train input and output parameters to JSON format.
/// </summary>
/// <remarks>
/// The ParameterEffect class provides an implementation of the IEffectProvider interface
/// that serializes train input and output parameters to JSON format.
///
/// This provider tracks metadata objects and serializes their input and output parameters
/// to JSON format when changes are saved. The serialized parameters are stored in the
/// metadata object's Input and Output properties, which can then be persisted to a database
/// or other storage medium.
///
/// This implementation is useful for capturing and storing the input and output parameters
/// of train executions, which can be used for auditing, debugging, and analytics purposes.
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
    /// This allows for capturing and storing the input and output parameters of train
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
                    metadata.Input = SerializeBounded(
                        inputObject,
                        options,
                        configuration.MaxParameterBytes
                    );
                }
                catch (PayloadTooLargeException ex)
                {
                    metadata.Input = ex.Placeholder;
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

        if (configuration.SaveOutputs && configuration.ShouldSaveOutputFor(metadata.Name))
        {
            var outputObject = metadata.GetOutputObject();
            if (outputObject is not null)
            {
                try
                {
                    metadata.Output = SerializeBounded(
                        outputObject,
                        options,
                        configuration.MaxParameterBytes
                    );
                }
                catch (PayloadTooLargeException ex)
                {
                    metadata.Output = ex.Placeholder;
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

    /// <summary>
    /// Serializes <paramref name="value"/> to a JSON string, aborting if it would exceed
    /// <paramref name="maxBytes"/> UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// When <paramref name="maxBytes"/> is <c>null</c> this is the unbounded string overload
    /// (unchanged historical behavior). When a ceiling is set it serializes through the
    /// <c>Stream</c> overload, which flushes to the underlying stream incrementally: the
    /// <see cref="ByteCeilingStream"/> counts the bytes as they are written and throws
    /// <see cref="PayloadTooLargeException"/> the moment the ceiling is crossed, so an oversized
    /// payload is never fully materialized. The runtime type is passed explicitly so a boxed
    /// value serializes polymorphically (matching the dynamic input/output objects).
    /// </remarks>
    private static string SerializeBounded(
        object value,
        JsonSerializerOptions options,
        int? maxBytes
    )
    {
        if (maxBytes is not int cap)
            return JsonSerializer.Serialize(value, value.GetType(), options);

        using var buffer = new MemoryStream();
        using (var ceiling = new ByteCeilingStream(buffer, cap))
            JsonSerializer.Serialize(ceiling, value, value.GetType(), options);

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// Thrown by <see cref="ByteCeilingStream"/> when a serialized parameter crosses the byte ceiling.
    /// </summary>
    private sealed class PayloadTooLargeException(int maxBytes)
        : Exception($"Serialized parameter exceeded {maxBytes} bytes.")
    {
        public string Placeholder { get; } =
            $$"""{"_truncated": true, "_maxBytes": {{maxBytes}}}""";
    }

    /// <summary>
    /// A write-only stream that forwards to an inner stream until a byte ceiling is crossed,
    /// then throws <see cref="PayloadTooLargeException"/>. Used to bound serialization work.
    /// </summary>
    /// <remarks>
    /// The ceiling behavior is validated behaviorally through <see cref="SerializeBounded"/> (see the
    /// over-cap and unbounded-output tests). The rest is <see cref="Stream"/> contract plumbing the
    /// serializer never calls on a write-only stream, so it is excluded from coverage.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private sealed class ByteCeilingStream(Stream inner, int maxBytes) : Stream
    {
        private long _written;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _written += buffer.Length;
            if (_written > maxBytes)
                throw new PayloadTooLargeException(maxBytes);
            inner.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;

        public override void Flush() => inner.Flush();

        public override long Length => _written;
        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
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
