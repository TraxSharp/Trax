using System.Linq.Expressions;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Effect.Utils;

/// <summary>
/// Provides standardized JSON serialization options for the Trax.Effect system.
/// </summary>
/// <remarks>
/// The TraxJsonSerializationOptions class defines a set of default serialization
/// options that are used throughout the Trax.Effect system. These options ensure
/// consistent serialization behavior across different components.
///
/// The default options include:
/// 1. Indented JSON output for better readability
/// 2. Inclusion of fields (not just properties) in serialization
/// 3. Omission of null values to reduce output size
/// 4. Custom converters for special types:
///    - Enums as strings for better readability and interoperability
///    - ValueTuples for proper serialization of tuple types
///    - System.Type objects for type information persistence
///
/// These options can be used by any component that needs to serialize or deserialize
/// JSON data in the Trax.Effect system, ensuring consistent behavior.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/save-train-parameters#serialization-members">SDK reference</seealso>
public static class TraxJsonSerializationOptions
{
    /// <summary>
    /// The default JSON serializer options for the Trax.Effect system, and what
    /// <c>SaveTrainParameters()</c> writes a run's input and output with. Read-only: copy it
    /// (<c>new JsonSerializerOptions(Default) { … }</c>) to change a setting.
    /// </summary>
    /// <remarks>
    /// These options are used by default throughout the system for JSON serialization
    /// and deserialization. They provide a consistent configuration that handles the
    /// special types and formatting requirements of the Trax.Effect system.
    ///
    /// The options include:
    /// - WriteIndented: true - Produces formatted, indented JSON for better readability
    /// - IncludeFields: true - Includes fields (not just properties) in serialization
    /// - DefaultIgnoreCondition: WhenWritingNull - Omits null values from the output
    /// - MaxDepth: 64 - System.Text.Json's own default, deep enough for nested inputs
    /// - No reference preservation: a stored input is a plain tree, with no $id or $values.
    ///   Rows written by versions that preserved references still read back (see
    ///   <c>StoredJson</c>)
    /// - Converters:
    ///   - JsonStringEnumConverter - Serializes enums as strings instead of numbers
    ///   - ValueTupleConverter - Handles serialization of ValueTuple types
    ///   - SystemTypeConverter - Handles serialization of System.Type objects
    /// </remarks>
    public static JsonSerializerOptions Default { get; internal set; } =
        Frozen(
            new()
            {
                WriteIndented = true,
                IncludeFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                MaxDepth = StoredDepth,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                Converters =
                {
                    new JsonStringEnumConverter(),
                    new ValueTupleConverter(),
                    new SystemTypeConverter(),
                },
            }
        );

    /// <summary>
    /// JSON serializer options for manifest properties and for a queued input's stored form: the
    /// same as <see cref="Default"/> with relaxed escaping. Read-only.
    /// </summary>
    public static JsonSerializerOptions ManifestProperties { get; internal set; } =
        Frozen(
            new()
            {
                WriteIndented = true,
                IncludeFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                MaxDepth = StoredDepth,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                Converters =
                {
                    new JsonStringEnumConverter(),
                    new ValueTupleConverter(),
                    new SystemTypeConverter(),
                },
            }
        );

    /// <summary>
    /// How deep a stored input or output may nest: System.Text.Json's own default. A host that
    /// reads its inputs deeper (its train parameter options' <c>MaxDepth</c>) can run such an
    /// input, but cannot queue it: the stored form is refused.
    /// </summary>
    internal const int StoredDepth = 64;

    private static JsonSerializerOptions Frozen(JsonSerializerOptions options)
    {
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>
    /// The options the junction logger uses to serialize a junction's output into <c>JunctionMetadata.OutputJson</c>
    /// when <c>serializeJunctionData</c> is on, wrapped by <see cref="TraxLogSerialization.ForLogging"/> so
    /// <c>[TraxSensitive]</c> members are masked. Compact, camelCase, nulls omitted, enums as strings, cycles ignored
    /// rather than preserved, depth capped at 8, and any <see cref="IDisposable"/> written as a placeholder by
    /// <see cref="DisposableConverter"/>. Process-wide and settable; replacing it affects every junction logger.
    /// </summary>
    public static JsonSerializerOptions JunctionLogging { get; internal set; } =
        new()
        {
            WriteIndented = false,
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            MaxDepth = 8,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(), new DisposableConverter() },
        };
}
