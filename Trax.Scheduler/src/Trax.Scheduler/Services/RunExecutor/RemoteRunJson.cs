using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Core.Exceptions;

namespace Trax.Scheduler.Services.RunExecutor;

/// <summary>
/// The JSON options for the remote-run wire, fixed by Trax rather than taken from the host.
/// </summary>
/// <remarks>
/// A worker used to write its response with whatever JSON options its host configured, so a host
/// that serializes enums as strings sent <c>FailureClass</c> as <c>"Transient"</c>, and a scheduler
/// reading with defaults failed on every error response that carried one. The writer now always
/// sends enums as their integers, and the reader accepts either form, so neither end depends on
/// the other's host configuration (see scheduler/0001). A Lambda worker's own response is
/// serialized by its function's Lambda serializer, which Trax does not control; the reader's
/// tolerance is what covers that path.
///
/// Public because it is the contract between two processes that ship in separate packages:
/// Trax.Scheduler.Lambda and Trax.Runner.Lambda read and write with it, and a package that
/// reached it through InternalsVisibleTo would break with MissingMethodException the first time a
/// consumer pulled in a newer Trax.Scheduler than it was built against. Both options are read-only,
/// so nothing that uses them can change the wire for everyone else.
/// </remarks>
public static class RemoteRunJson
{
    /// <summary>What a worker writes: web defaults, enums as integers.</summary>
    public static readonly JsonSerializerOptions Write = ReadOnly(
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
    );

    /// <summary>
    /// What a scheduler reads: web defaults, enums as integers or names, and a failure class it
    /// does not know read as <see cref="FailureClass.Unclassified"/>.
    /// </summary>
    /// <remarks>
    /// A newer worker can send a class this scheduler predates. Failing the read would throw
    /// away the worker's real error with it, and storing an unknown integer would fail against
    /// the Postgres enum, so the class degrades to unclassified and the failure survives.
    /// </remarks>
    public static readonly JsonSerializerOptions Read = ReadOnly(
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new TolerantFailureClassConverter(), new JsonStringEnumConverter() },
        }
    );

    private static JsonSerializerOptions ReadOnly(JsonSerializerOptions options)
    {
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed class TolerantFailureClassConverter : JsonConverter<FailureClass>
    {
        public override FailureClass Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
                return Enum.IsDefined(typeof(FailureClass), number)
                    ? (FailureClass)number
                    : FailureClass.Unclassified;

            if (
                reader.TokenType == JsonTokenType.String
                && Enum.TryParse<FailureClass>(reader.GetString(), ignoreCase: true, out var named)
                && Enum.IsDefined(named)
            )
                return named;

            return FailureClass.Unclassified;
        }

        public override void Write(
            Utf8JsonWriter writer,
            FailureClass value,
            JsonSerializerOptions options
        ) => writer.WriteNumberValue((int)value);
    }
}
