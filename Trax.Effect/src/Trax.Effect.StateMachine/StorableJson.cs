using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Finds the values a snapshot context may not carry even though they are valid JSON: a number outside
/// the range of a double (<c>1e400</c>), which has no canonical wire, and a NUL character in a string or
/// a key, which a Postgres <c>jsonb</c> column refuses. <see cref="SnapshotMachine{TState,TTrigger}.Rehydrate"/>
/// refuses both as <c>malformed</c>, and the persistence layer checks an advance's result the same way
/// before it writes, so a stored draft always serializes and always loads.
/// </summary>
public static class StorableJson
{
    /// <summary>
    /// Returns a message naming the first value that cannot be stored, or <c>null</c> when every value
    /// in <paramref name="node"/> can. Never throws.
    /// </summary>
    public static string? Problem(JsonNode? node)
    {
        try
        {
            return Walk(node);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return "The snapshot holds a value that cannot be written as JSON.";
        }
    }

    private static string? Walk(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    if (kv.Key.Contains('\0'))
                        return "A key holds a NUL character, which cannot be stored.";
                    if (Walk(kv.Value) is { } problem)
                        return problem;
                }
                return null;
            case JsonArray arr:
                foreach (var item in arr)
                    if (Walk(item) is { } problem)
                        return problem;
                return null;
            default:
                return Scalar(node.AsValue());
        }
    }

    private static string? Scalar(JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                return value.GetValue<string>().Contains('\0')
                    ? "A string holds a NUL character, which cannot be stored."
                    : null;
            case JsonValueKind.Number:
                // A parsed number is read from its text, so 1e400 comes back as infinity rather than failing
                // to parse. A CLR double backing (NaN, infinity) is read directly, since its text may throw.
                var finite = value.TryGetValue<double>(out var d)
                    ? double.IsFinite(d)
                    : double.TryParse(
                        value.ToJsonString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out d
                    ) && double.IsFinite(d);
                return finite ? null : "A number is outside the range of a double.";
            default:
                return null;
        }
    }
}
