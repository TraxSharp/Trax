using System.Text;
using System.Text.Json;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Utils;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainExecution;

/// <summary>
/// Reads a caller's train input JSON the way every Trax entry point reads it, so a queue, a run
/// and a surface that submits work itself accept and refuse the same JSON.
/// </summary>
/// <remarks>
/// <see cref="TrainExecutionService"/> reads every <c>RunAsync</c>, <c>QueueAsync</c> and
/// <c>PrepareAsync</c> input through <see cref="Read"/>. A host or package that takes input JSON
/// on another path should call it too, rather than keep a copy of these rules, and one that
/// builds an input object and hands it on as JSON writes it with <see cref="Write"/>.
/// </remarks>
public static class TrainInputReader
{
    /// <summary>
    /// How many times <c>MaxInputJsonBytes</c> the stored form of a queued input may be. An
    /// enqueue writes the parsed input back out indented and with every member present, which is
    /// larger than a caller's compact JSON, so the stored form is allowed this much room and no
    /// more; an input that would be stored larger is refused.
    /// </summary>
    public const int StoredInputGrowthFactor = 4;

    /// <summary>What a missing input is read as.</summary>
    private const string EmptyInput = "{}";

    private static CallerInputOptions? _inputOptions;

    /// <summary>
    /// Reads <paramref name="inputJson"/> as an instance of the train's input type.
    /// </summary>
    /// <param name="inputJson">
    /// The caller's JSON. Null, empty or whitespace is read as <c>{}</c>, which is refused when the
    /// input type needs values to be built.
    /// </param>
    /// <param name="registration">The train whose input type is read.</param>
    /// <param name="maxInputJsonBytes">
    /// The size cap, in UTF-8 bytes, normally <c>MediatorConfiguration.MaxInputJsonBytes</c>.
    /// </param>
    /// <returns>The input, never null.</returns>
    /// <remarks>
    /// The rules: the size cap is checked before anything is parsed; property names match
    /// whatever their case, and a property given twice (in any casing) is refused
    /// (Trax.Docs/adr/0023); JSON reference metadata (<c>$id</c>, <c>$ref</c>, <c>$values</c>)
    /// is not honoured, so an input is exactly the tree the caller wrote (a saved input, which
    /// carries it, goes through <see cref="ResolveSavedInput"/> first); and a JSON
    /// <c>null</c> is refused. Call it after authorization, so a caller who may not use the
    /// train learns nothing about its input from a parse error.
    /// </remarks>
    /// <exception cref="TrainInputValidationException">The JSON is larger than the cap.</exception>
    /// <exception cref="JsonException">
    /// The JSON cannot be read as the input type, is <c>null</c>, or is missing and the input
    /// type cannot be built from <c>{}</c>.
    /// </exception>
    public static object Read(
        string? inputJson,
        TrainRegistration registration,
        int maxInputJsonBytes
    )
    {
        var missing = string.IsNullOrWhiteSpace(inputJson);
        var json = missing ? EmptyInput : inputJson!;

        // Before deserialization, so oversized JSON never reaches the deserializer. Byte length
        // (UTF-8) is the bounded resource: char length would miscount surrogate pairs and
        // multi-byte sequences.
        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > maxInputJsonBytes)
            throw new TrainInputValidationException(
                registration.ServiceTypeName,
                byteCount,
                maxInputJsonBytes
            );

        return Deserialize(json, registration, missing);
    }

    /// <summary>
    /// Writes <paramref name="input"/> as JSON that <see cref="Read"/> reads back as the same
    /// input.
    /// </summary>
    /// <param name="input">The input, an instance of <paramref name="inputType"/>.</param>
    /// <param name="inputType">The train's input type.</param>
    /// <returns>The input as a plain JSON tree.</returns>
    /// <remarks>
    /// It writes with the options <see cref="Read"/> reads with, so it follows the host's
    /// naming policy and converters but never writes reference metadata, which the host's
    /// options (<c>TraxJsonSerializationOptions.Default</c> unless the host gives others) do.
    /// A surface that builds an input object and hands it to <c>ITrainExecutionService</c> as
    /// JSON writes it here: written with the host's options instead, every list in it is
    /// refused.
    /// </remarks>
    public static string Write(object? input, Type inputType)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        return JsonSerializer.Serialize(input, inputType, InputOptions().Given);
    }

    /// <summary>
    /// Turns a run's saved input (the <c>Input</c> column <c>SaveTrainParameters()</c> writes)
    /// into JSON that <see cref="Read"/> reads as the input the run was given.
    /// </summary>
    /// <param name="savedInputJson">The saved input.</param>
    /// <param name="registration">The train the input was saved for.</param>
    /// <param name="maxInputJsonBytes">
    /// The size cap, in UTF-8 bytes, that <see cref="Read"/> will hold the result to, normally
    /// <c>MediatorConfiguration.MaxInputJsonBytes</c>.
    /// </param>
    /// <returns>
    /// The input as a plain, compact JSON tree. One saved without reference metadata keeps every
    /// member as it is and loses only insignificant whitespace; one that is not JSON is returned
    /// unchanged, for <see cref="Read"/> to refuse.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>SaveTrainParameters()</c> writes with <c>TraxJsonSerializationOptions.Default</c>, which
    /// preserves references: every object carries an <c>$id</c>, a list is written as
    /// <c>{"$id":"2","$values":[...]}</c>, and a second occurrence of the same object as
    /// <c>{"$ref":"3"}</c>. <see cref="Read"/> does not honour that metadata in a caller's
    /// input, so read directly a saved list is refused and a <c>$ref</c> reads back as an object
    /// with every member at its default. Anything that reads a saved input back as a train's
    /// input (a re-queue) passes it through here first.
    /// </para>
    /// <para>
    /// Only a saved input whose root object carries an <c>$id</c>, which a writer that preserves
    /// references always gives it, has its metadata resolved; any other is written back compact,
    /// members untouched, because a <c>jsonb</c> column hands it back with whitespace that can put
    /// an input saved at the cap over it. The metadata is recognised wherever it sits in its
    /// object: the writer puts <c>$id</c> first, but a <c>jsonb</c> column orders an object's
    /// properties shortest name first, so an input with a property such as <c>id</c> or <c>x</c>
    /// comes back with <c>$id</c> after it. Each <c>$id</c> is dropped, each
    /// <c>$values</c> object is written as its array, and each <c>$ref</c> is written as a full
    /// copy of the value it names, so the result is a tree: no two members of the input read from
    /// it share an object. A <c>$ref</c> to a value that contains it has no such tree and is
    /// refused. Writing stops the moment the result is larger than
    /// <paramref name="maxInputJsonBytes"/>, so references that copy one value many times over
    /// cannot make a small saved input a large one. Nothing here depends on the input type, so it
    /// reveals nothing about it and may run before the caller is authorized.
    /// </para>
    /// </remarks>
    /// <exception cref="JsonException">
    /// The saved input is not JSON, its reference metadata is malformed or names a value that is
    /// not there, or a value refers to itself.
    /// </exception>
    /// <exception cref="TrainInputValidationException">
    /// The plain form is larger than <paramref name="maxInputJsonBytes"/>.
    /// </exception>
    public static string ResolveSavedInput(
        string savedInputJson,
        TrainRegistration registration,
        int maxInputJsonBytes
    )
    {
        ArgumentNullException.ThrowIfNull(savedInputJson);
        ArgumentNullException.ThrowIfNull(registration);

        if (SavedInputReferences.Present(savedInputJson))
            return SavedInputReferences.Resolve(savedInputJson, registration, maxInputJsonBytes);

        try
        {
            // Rewritten even without metadata: read back from jsonb, the saved input carries
            // whitespace it was not saved with, which can put an input saved at the cap over it.
            return SavedInputReferences.Compact(savedInputJson, registration, maxInputJsonBytes);
        }
        catch (JsonException)
        {
            // Not JSON at all: left for the reader to refuse with its own message.
            return savedInputJson;
        }
    }

    /// <summary>
    /// Writes a read input in the form a work queue entry stores, refusing it when that form is
    /// larger than <see cref="StoredInputGrowthFactor"/> times <paramref name="maxInputJsonBytes"/>.
    /// </summary>
    /// <remarks>
    /// The bytes are counted as they are written and writing stops the moment the cap is crossed,
    /// so an input whose stored form would be far over the cap is refused without that form ever
    /// being built. How much larger the stored form is than the caller's JSON depends on the input
    /// type: an empty object of a type with many members is written with every one of them.
    /// </remarks>
    /// <exception cref="TrainInputValidationException">
    /// The stored form is over its cap; <c>MaxBytes</c> is that cap and <c>ObservedBytes</c> how
    /// much had been written when writing stopped, which is more than the cap.
    /// </exception>
    internal static string WriteForStorage(
        object input,
        TrainRegistration registration,
        int maxInputJsonBytes
    )
    {
        var storedCap = (int)
            Math.Min((long)maxInputJsonBytes * StoredInputGrowthFactor, int.MaxValue);

        using var buffer = new MemoryStream();

        try
        {
            using var ceiling = new ByteCeilingStream(buffer, storedCap);
            JsonSerializer.Serialize(
                ceiling,
                input,
                registration.InputType,
                TraxJsonSerializationOptions.ManifestProperties
            );
        }
        catch (StoredInputTooLargeException tooLarge)
        {
            throw new TrainInputValidationException(
                registration.ServiceTypeName,
                tooLarge.Written,
                storedCap
            );
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private sealed class StoredInputTooLargeException(int written) : Exception
    {
        public int Written { get; } = written;
    }

    /// <summary>
    /// A write-only stream that forwards to an inner stream until more than
    /// <paramref name="maxBytes"/> have been written, then throws instead of writing. The
    /// serializer flushes to its stream as it goes, so the throw ends serialization early.
    /// </summary>
    private sealed class ByteCeilingStream(Stream inner, int maxBytes) : Stream
    {
        private long _written;

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _written += buffer.Length;

            if (_written > maxBytes)
                throw new StoredInputTooLargeException((int)Math.Min(_written, int.MaxValue));

            inner.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

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

    private static object Deserialize(
        string inputJson,
        TrainRegistration registration,
        bool missing
    )
    {
        object? input;

        if (missing)
        {
            // A missing input stands in for an input with no values, which is only honest for a
            // type that needs none. System.Text.Json builds a positional record from {} with every
            // constructor parameter at its default, so without this a train taking
            // record RenamePlayer(string Id, string NewName) would be queued with a null Id.
            // Respecting required constructor parameters refuses exactly that, and leaves Unit,
            // an input with only settable properties, and parameters with defaults unaffected.
            try
            {
                input = JsonSerializer.Deserialize(
                    inputJson,
                    registration.InputType,
                    InputOptions().Missing
                );
            }
            catch (JsonException refused)
            {
                throw new JsonException(
                    $"No input was given, and {registration.InputTypeName} cannot be built "
                        + $"without one: {refused.Message}",
                    refused
                );
            }
        }
        else
        {
            try
            {
                input = JsonSerializer.Deserialize(
                    inputJson,
                    registration.InputType,
                    InputOptions().Given
                );
            }
            catch (JsonException refused) when (SavedInputReferences.Present(inputJson))
            {
                // A list written with reference metadata fails as "could not be converted",
                // which names neither the metadata nor the way out. Checked only once the
                // input has been refused, so an input that reads costs nothing extra.
                throw new JsonException(
                    $"{refused.Message} The input carries JSON reference metadata ($id, "
                        + "$values, $ref), which a train input does not accept. Write it "
                        + "without, as TrainInputReader.Write does; a run's saved input is "
                        + "turned into a plain one by TrainInputReader.ResolveSavedInput.",
                    refused.Path,
                    refused.LineNumber,
                    refused.BytePositionInLine,
                    refused
                );
            }
        }

        // A JSON null is well-formed but is not an input, so it is reported the way any other
        // input the train cannot use is: as a JSON problem the caller can fix.
        if (input is null)
            throw new JsonException(
                $"InputJson deserialized to null. Expected an instance of {registration.InputTypeName}."
            );

        return input;
    }

    /// <summary>
    /// How a caller's input is read: the system options, with property names matched whatever
    /// their case, a property given twice (in any casing) refused, and no reference handling, so
    /// the input is the tree the caller wrote. The missing-input reading also respects required constructor parameters. Rebuilt only
    /// if the system options object itself is replaced.
    /// </summary>
    private static CallerInputOptions InputOptions()
    {
        var source = TraxEffectConfiguration.StaticSystemJsonSerializerOptions;
        var cached = _inputOptions;

        if (cached is not null && ReferenceEquals(cached.Source, source))
            return cached;

        var given = new JsonSerializerOptions(source)
        {
            PropertyNameCaseInsensitive = true,
            AllowDuplicateProperties = false,
            ReferenceHandler = null,
        };
        var missing = new JsonSerializerOptions(given)
        {
            RespectRequiredConstructorParameters = true,
        };

        var built = new CallerInputOptions(source, given, missing);
        _inputOptions = built;
        return built;
    }

    private sealed record CallerInputOptions(
        JsonSerializerOptions Source,
        JsonSerializerOptions Given,
        JsonSerializerOptions Missing
    );
}
