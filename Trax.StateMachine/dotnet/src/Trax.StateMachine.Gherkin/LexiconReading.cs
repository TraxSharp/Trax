using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin.Lexical;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// What reading a lexicon gave: the glossary it elaborates to and that glossary's canonical JSON, the file
/// <c>trax machine from-gherkin</c> writes beside the IRs; or the problems, each at its lexicon line, and no glossary.
/// </summary>
public sealed record LexiconReading(
    FeatureGlossary? Glossary,
    string? Json,
    IReadOnlyList<FeatureProblem> Problems
)
{
    /// <summary>Whether the lexicon elaborated to a glossary.</summary>
    public bool Succeeded => Glossary is not null;

    /// <summary>The glossary, for a caller that cannot go on without one (a test run's hook).</summary>
    /// <exception cref="FormatException">The lexicon has problems; the message lists each at its file and line.</exception>
    public FeatureGlossary GlossaryOrThrow() =>
        Glossary
        ?? throw new FormatException(
            "The lexicon has problems:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, Problems.Select(p => "  " + p))
        );
}
