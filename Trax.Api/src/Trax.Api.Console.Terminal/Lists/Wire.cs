using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>Values as the host writes them: an enum by its GraphQL name, a record as its JSON, a time in UTC.</summary>
internal static class Wire
{
    /// <summary>The names the host writes an enum's values as, in declaration order.</summary>
    public static IReadOnlyList<string> Names<TEnum>()
        where TEnum : struct, Enum =>
        typeof(TEnum)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? f.Name)
            .ToList();

    /// <summary>The name the host writes <paramref name="value"/> as.</summary>
    public static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonSerializer.Serialize(value, TraxJson.Options).Trim('"');

    /// <summary>The enum value the host writes as <paramref name="text"/>, matched ignoring case.</summary>
    /// <exception cref="InvalidOperationException">No value is written so.</exception>
    public static TEnum Parse<TEnum>(string filter, string text)
        where TEnum : struct, Enum
    {
        var name = Names<TEnum>()
            .FirstOrDefault(n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is null
            ? throw new InvalidOperationException(
                $"{filter} '{text}' is not one of {string.Join(", ", Names<TEnum>())}."
            )
            : JsonSerializer.Deserialize<TEnum>($"\"{name}\"", TraxJson.Options);
    }

    /// <summary>A whole number a filter or an id takes.</summary>
    public static long Long(string filter, string text) =>
        long.TryParse(
            text.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : throw new InvalidOperationException($"{filter} '{text}' is not a whole number.");

    /// <summary>true or false.</summary>
    public static bool Bool(string filter, string text) =>
        bool.TryParse(text.Trim(), out var value)
            ? value
            : throw new InvalidOperationException($"{filter} '{text}' is not true or false.");

    /// <summary>A time as the terminal shows it: UTC, to the second.</summary>
    public static string Time(DateTimeOffset? at) =>
        at?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";

    /// <summary>A record as its JSON, as the host wrote it.</summary>
    public static JsonNode? Json<T>(T value) =>
        JsonSerializer.SerializeToNode(value, TraxJson.Options);

    /// <summary>
    /// A record as label and value pairs, depth first, a nested field's label its path (<c>waitingOn.trainState</c>).
    /// A list is shown as its JSON. For a detail view, which shows whatever the document selects.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> Fields<T>(T value)
    {
        var fields = new List<(string, string)>();
        void Walk(string prefix, JsonNode? node)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (key, child) in o)
                        Walk(prefix.Length == 0 ? key : $"{prefix}.{key}", child);
                    break;
                case JsonArray a:
                    fields.Add((prefix, a.ToJsonString()));
                    break;
                case JsonValue v when v.TryGetValue<string>(out var text):
                    fields.Add((prefix, text));
                    break;
                case null:
                    fields.Add((prefix, ""));
                    break;
                default:
                    fields.Add((prefix, node.ToJsonString()));
                    break;
            }
        }
        Walk("", Json(value));
        return fields;
    }
}
