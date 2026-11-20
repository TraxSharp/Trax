using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Cli.Machines;

/// <summary>
/// The languages <c>trax machine generate --lang</c> takes, the first the default. Read from the engine's
/// <c>Trax.StateMachine/ts/tools/twin-languages.json</c>, embedded at build time, which generate-twin.mjs reads too,
/// so the CLI's validation and <c>--help</c> list what the tool it runs writes.
/// </summary>
internal static class TwinLanguages
{
    // Read from the assembly once, on first use.
    private static readonly Lazy<Task<IReadOnlyList<TwinLanguage>>> Languages = new(ReadAsync);

    public static Task<IReadOnlyList<TwinLanguage>> AllAsync() => Languages.Value;

    public static async Task<string> DefaultAsync() =>
        (await AllAsync().ConfigureAwait(false))[0].Lang;

    public static async Task<bool> ContainsAsync(string lang) =>
        (await AllAsync().ConfigureAwait(false)).Any(l =>
            string.Equals(l.Lang, lang, StringComparison.Ordinal)
        );

    /// <summary><c>'a' or 'b'</c>, <c>'a', 'b' or 'c'</c>: as generate-twin.mjs's <c>oneOf</c> writes it.</summary>
    public static string OneOf(IReadOnlyList<string> names) => Or([.. names.Select(n => $"'{n}'")]);

    /// <summary>The <c>--lang</c> option's help: each language and what it writes, the default first.</summary>
    public static async Task<string> HelpAsync() => Help(await AllAsync().ConfigureAwait(false));

    internal static string Help(IReadOnlyList<TwinLanguage> all) =>
        "The typed view --twin-out writes: "
        + Or([.. all.Select((l, i) => $"'{l.Lang}' ({(i == 0 ? "default; " : "")}{l.Writes})")])
        + ".";

    private static string Or(IReadOnlyList<string> items) =>
        items.Count < 2
            ? string.Concat(items)
            : string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1];

    private static async Task<IReadOnlyList<TwinLanguage>> ReadAsync()
    {
        var stream =
            typeof(TwinLanguages).Assembly.GetManifestResourceStream("Trax.Cli.TwinLanguages.json")
            ?? throw new InvalidOperationException("trax was built without twin-languages.json.");
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer
                    .DeserializeAsync<List<TwinLanguage>>(stream)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException("twin-languages.json is empty.");
        }
    }
}
