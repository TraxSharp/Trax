using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>One phrase of the glossary and the steps that say it, by file and line. A phrase no step says has none.</summary>
internal sealed record PhraseUse(
    string Part,
    string Phrase,
    IReadOnlyList<(string File, long Line)> Steps
);
