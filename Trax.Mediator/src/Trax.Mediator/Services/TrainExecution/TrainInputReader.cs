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
/// on another path should call it too, rather than keep a copy of these rules.
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
    /// is not honoured, so an input is exactly the tree the caller wrote; and a JSON
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
    /// Writes a read input in the form a work queue entry stores, refusing it when that form is
    /// larger than <see cref="StoredInputGrowthFactor"/> times <paramref name="maxInputJsonBytes"/>.
    /// </summary>
    /// <exception cref="TrainInputValidationException">
    /// The stored form is over its cap; <c>MaxBytes</c> is that cap and <c>ObservedBytes</c> the
    /// stored form's size.
    /// </exception>
    internal static string WriteForStorage(
        object input,
        TrainRegistration registration,
        int maxInputJsonBytes
    )
    {
        var serialized = JsonSerializer.Serialize(
            input,
            registration.InputType,
            TraxJsonSerializationOptions.ManifestProperties
        );

        var storedCap = (int)
            Math.Min((long)maxInputJsonBytes * StoredInputGrowthFactor, int.MaxValue);
        var byteCount = Encoding.UTF8.GetByteCount(serialized);

        if (byteCount > storedCap)
            throw new TrainInputValidationException(
                registration.ServiceTypeName,
                byteCount,
                storedCap
            );

        return serialized;
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
            input = JsonSerializer.Deserialize(
                inputJson,
                registration.InputType,
                InputOptions().Given
            );
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
