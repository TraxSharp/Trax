using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Effect.StateMachine.Tests.Helpers;

/// <summary>
/// The CLR's verdict on a machine record's JSON: whether the typed surface reads it into the record, and whether what
/// it writes back is accepted again. The options are those of <c>MachineSurface.Json</c> in Trax.Api.GraphQL, which
/// reads a stored context and writes an input with them (this folder cannot reference it): web defaults, nulls left
/// out, enums as names, a TimeSpan as an ISO-8601 duration, computed properties left out. Reading also refuses a
/// member the record does not hold, since an engine that accepted it would hand reducers a value the record drops.
/// </summary>
public static class TypedSurfaceJson
{
    /// <summary>How the typed surface reads and writes a machine's records.</summary>
    public static readonly JsonSerializerOptions Options = Create(JsonUnmappedMemberHandling.Skip);

    private static readonly JsonSerializerOptions Strict = Create(
        JsonUnmappedMemberHandling.Disallow
    );

    private static JsonSerializerOptions Create(JsonUnmappedMemberHandling unmapped)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = unmapped,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { SchemaReflection.LeaveOutComputedProperties },
            },
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new IsoDurationJsonConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// Whether <paramref name="json"/>, stored as the engines store it (the canonical wire, numbers as JavaScript
    /// writes them), reads into <typeparamref name="T"/>; <paramref name="written"/> is the record written back.
    /// </summary>
    public static bool Reads<T>(JsonNode json, out JsonObject? written, out string? why)
    {
        written = null;
        why = null;
        try
        {
            var record = JsonSerializer.Deserialize<T>(CanonicalJson.Serialize(json), Strict);
            written = JsonSerializer.SerializeToNode(record, Options) as JsonObject;
            return true;
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or ArgumentException
                        or InvalidOperationException
                        or FormatException
                        or OverflowException
            )
        {
            why = ex.Message;
            return false;
        }
    }
}
