using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin.Lexical;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Where each key of an elaborated glossary came from in its lexicon, so a problem the glossary reader or derivation
/// finds is reported at the lexicon line, in the lexicon's words.
/// </summary>
internal sealed partial class LexiconSource(string path, IReadOnlyDictionary<string, int> lines)
{
    internal string Path { get; } = path;

    /// <summary>The line of the deepest key a pointer passes through that the lexicon wrote, or 0.</summary>
    internal int LineOf(string? pointer)
    {
        var at = pointer ?? "";
        while (true)
        {
            if (lines.TryGetValue(at, out var line))
                return line;
            var slash = at.LastIndexOf('/');
            if (slash < 0)
                return 0;
            at = at[..slash];
        }
    }

    // The glossary's keys, as the lexicon says them.
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal)
    {
        ["version"] = "Version:",
        ["actors"] = "Actor:",
        ["roles"] = "Roles:",
        ["anyAuthenticated"] = "Any signed-in",
        ["ownedBy"] = "Owned by:",
        ["system"] = "System",
        ["start"] = "Started by:",
        ["initial"] = "Begins in:",
        ["context"] = "Context:",
        ["initialContext"] = "Starts with:",
        ["states"] = "State:",
        ["requires"] = "Requires:",
        ["triggers"] = "Trigger:",
        ["input"] = "Input:",
        ["reduce"] = "Sets:",
        ["effect"] = "Effect:",
        ["guard"] = "Guarded by:",
        ["guards"] = "Guard:",
        ["holds"] = "Holds:",
        ["fails"] = "Fails:",
        ["invokes"] = "Runs:",
        ["output"] = "Returns:",
        ["outcomes"] = "Outcome:",
        ["examples"] = "Example:",
        ["links"] = "Link:",
        ["rules"] = "Rule:",
        ["reductions"] = "Reduction:",
        ["migrations"] = "Migrates from:",
        ["differential"] = "Differential",
        ["views"] = "View:",
        ["reads"] = "Reads:",
        ["fields"] = "Field:",
        ["labels"] = "Label:",
    };

    /// <summary>
    /// A glossary problem in the lexicon's terms: led by the keyword of the deepest key it is about, and with any other
    /// key it names given as its line.
    /// </summary>
    internal string Message(string? pointer, string problem)
    {
        var said = Pointer.Replace(
            problem,
            m =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"line {LineOf(m.Groups["pointer"].Value)}{m.Groups["end"].Value}"
                )
        );
        var keyword = (pointer ?? "")
            .Split('/')
            .Reverse()
            .Select(s => Keywords.GetValueOrDefault(s))
            .FirstOrDefault(k => k is not null);
        return keyword is null ? said : $"'{keyword}' {said}";
    }

    // A pointer the reader names inside its message: "is also at /machines/order/states/Draft: a step …".
    [GeneratedRegex(
        @"(?<pointer>/(?:actors|machines|views)/.*?)(?<end>:\s|\.$|$)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Pointer { get; }
}
