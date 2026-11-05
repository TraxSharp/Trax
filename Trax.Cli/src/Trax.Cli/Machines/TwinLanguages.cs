using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Cli.Machines;

/// <summary>A language <c>--lang</c> takes, and what <c>--twin-out</c> writes in it.</summary>
internal sealed record TwinLanguage(
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("writes")] string Writes
);

/// <summary>
/// The languages <c>trax machine generate --lang</c> takes, the first the default. Read from the engine's
/// <c>Trax.StateMachine/ts/tools/twin-languages.json</c>, embedded at build time, which generate-twin.mjs reads too,
/// so the CLI's validation and <c>--help</c> list what the tool it runs writes.
/// </summary>
internal static class TwinLanguages
{
    public static IReadOnlyList<TwinLanguage> All { get; } = Read();

    public static string Default => All[0].Lang;

    public static bool Contains(string lang) => All.Any(l => l.Lang == lang);

    /// <summary><c>'a' or 'b'</c>, <c>'a', 'b' or 'c'</c>: as generate-twin.mjs's <c>oneOf</c> writes it.</summary>
    public static string OneOf(IReadOnlyList<string> names) => Or([.. names.Select(n => $"'{n}'")]);

    /// <summary>The <c>--lang</c> option's help: each language and what it writes, the default first.</summary>
    public static string Help() => Help(All);

    internal static string Help(IReadOnlyList<TwinLanguage> all) =>
        "The typed view --twin-out writes: "
        + Or([.. all.Select((l, i) => $"'{l.Lang}' ({(i == 0 ? "default; " : "")}{l.Writes})")])
        + ".";

    private static string Or(IReadOnlyList<string> items) =>
        items.Count < 2
            ? string.Concat(items)
            : string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1];

    private static IReadOnlyList<TwinLanguage> Read()
    {
        using var stream =
            typeof(TwinLanguages).Assembly.GetManifestResourceStream("Trax.Cli.TwinLanguages.json")
            ?? throw new InvalidOperationException("trax was built without twin-languages.json.");
        return JsonSerializer.Deserialize<List<TwinLanguage>>(stream)
            ?? throw new InvalidOperationException("twin-languages.json is empty.");
    }
}
