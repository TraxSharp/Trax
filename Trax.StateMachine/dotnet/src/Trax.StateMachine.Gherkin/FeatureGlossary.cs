using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// A glossary: the actors, machines, phrases and views a team's features are written in (ADR
/// <c>Trax.Docs/adr/0058</c>). Read one with <see cref="Read"/>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-machines">SDK reference</seealso>
public sealed class FeatureGlossary
{
    private FeatureGlossary(Glossary glossary, LexiconSource? source)
    {
        Inner = glossary;
        Source = source;
    }

    internal Glossary Inner { get; }

    /// <summary>The lexicon the glossary was elaborated from, or null for one read from its JSON.</summary>
    internal LexiconSource? Source { get; }

    /// <summary>Reads a glossary from its JSON text.</summary>
    /// <exception cref="FormatException">The text is not a glossary; the message names the key.</exception>
    public static FeatureGlossary Read(string json) => new(Glossary.Read(json), source: null);

    internal static FeatureGlossary From(Glossary glossary, LexiconSource source) =>
        new(glossary, source);

    // A problem derivation found with the glossary rather than a feature, at the lexicon line that wrote the key it is
    // about, when the glossary came from a lexicon.
    internal FeatureProblem Locate(Problem problem) =>
        Source is { } source
        && string.Equals(problem.File, Problem.Glossary, StringComparison.Ordinal)
            ? new FeatureProblem(
                source.Path,
                source.LineOf(problem.Pointer),
                problem.Pointer is null
                    ? problem.Message
                    : source.Message(problem.Pointer, problem.Message)
            )
            : FeatureProblem.Of(problem);
}
