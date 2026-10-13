using System.Text.Json;

namespace Trax.Samples.PersistedOperations.E2E.Fixtures;

/// <summary>
/// The greet_v1 documents the sample's client uploads, and how to read the answer.
/// </summary>
public static class GreetDocuments
{
    /// <summary>The document the manifest ships: answers "Hey Alice!".</summary>
    public const string Casual =
        "query Greet($name: String!) { discover { greeting { greet(input: { name: $name, style: CASUAL }) { greeting } } } }";

    /// <summary>The operator's fix: same response shape, answers "Good day, Alice.".</summary>
    public const string Formal =
        "query Greet($name: String!) { discover { greeting { greet(input: { name: $name, style: FORMAL }) { greeting } } } }";

    /// <summary>
    /// An edit that renames the field the app reads (<c>greeting</c> becomes <c>message</c>),
    /// which the shape-diff guardrail refuses.
    /// </summary>
    public const string RenamedField =
        "query Greet($name: String!) { discover { greeting { greet(input: { name: $name, style: FORMAL }) { message: greeting } } } }";

    /// <summary>
    /// A document sent inline instead of by id, as a caller without the manifest would send it.
    /// RequirePersisted refuses it.
    /// </summary>
    public const string Inline =
        "query { discover { greeting { greet(input: { name: \"Eve\", style: FORMAL }) { greeting } } } }";

    /// <summary>The variables the app sends with an id.</summary>
    public static object Variables(string name) => new { name };

    /// <summary>The greeting a response carries, or the raw response when it carries none.</summary>
    public static string GreetingOf(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("discover", out var discover)
        && discover.ValueKind == JsonValueKind.Object
            ? discover
                .GetProperty("greeting")
                .GetProperty("greet")
                .GetProperty("greeting")
                .GetString()
                ?? doc.RootElement.GetRawText()
            : doc.RootElement.GetRawText();
}
