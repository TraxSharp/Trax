using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>One phrase of the glossary and the steps that say it, by file and line. A phrase no step says has none.</summary>
internal sealed record PhraseUse(
    string Part,
    string Phrase,
    IReadOnlyList<(string File, long Line)> Steps
);

/// <summary>
/// Which steps say each phrase of the glossary, as Cucumber's usage report lists step definitions: an actor's noun, a
/// machine's start, each state, guard, trigger and outcome phrase, and each view field. Sorted by part, so the report
/// reads the same on every run.
/// </summary>
internal static class Usage
{
    private static readonly Comparer<(string File, long Line)> ByFileAndLine = Comparer<(
        string File,
        long Line
    )>.Create(
        (a, b) =>
            string.CompareOrdinal(a.File, b.File) is var byFile and not 0
                ? byFile
                : a.Line.CompareTo(b.Line)
    );

    internal static (IReadOnlyList<PhraseUse> Phrases, IReadOnlyList<Problem> Problems) Find(
        Glossary glossary,
        IReadOnlyList<(string Path, string Text)> features
    )
    {
        var (scenarios, problems) = FeatureFiles.Parse(features);
        var resolver = new StepResolver(glossary);
        var uses = new Dictionary<string, SortedSet<(string, long)>>(StringComparer.Ordinal);
        var phrases = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Phrase(string part, string phrase)
        {
            phrases[part] = phrase;
            uses.TryAdd(part, new SortedSet<(string, long)>(ByFileAndLine));
        }

        foreach (var actor in glossary.Actors.Values)
            Phrase($"actor {actor.Name}", $"is {actor.Noun}");
        foreach (var machine in glossary.Machines.Values)
        {
            var m = machine.Id;
            if (machine.Start is { } start)
                Phrase($"{m} start", start);
            foreach (var (state, phrase) in machine.States)
                Phrase($"{m} state {state}", phrase);
            foreach (var guard in machine.Guards.Values)
            {
                Phrase($"{m} guard {guard.Name} holds", guard.Holds);
                Phrase($"{m} guard {guard.Name} fails", guard.Fails);
            }
            foreach (var trigger in machine.Triggers.Values)
                Phrase($"{m} trigger {trigger.Name}", trigger.Phrase);
            foreach (var invoke in machine.Invokes.Values)
            foreach (var (phrase, outcome) in invoke.Outcomes)
                Phrase($"{m} outcome {invoke.State} {outcome.Kind}", phrase);
        }
        foreach (var view in glossary.Views.Values)
        foreach (var field in view.Fields)
            Phrase($"view {view.Name} {field.Name}", $"show {field.Name}");

        // Seen once per Background step it was resolved in, however many scenarios share it.
        foreach (var scenario in scenarios)
        foreach (var (step, meaning) in Walk.Run(scenario, glossary, resolver).Steps)
        {
            var part = meaning switch
            {
                Meaning.Named n => $"actor {n.Actor}",
                Meaning.Start s => $"{s.Machine} start",
                Meaning.InState s => $"{s.Machine} state {s.State}",
                Meaning.Guard g => $"{g.Machine} guard {g.Name} {(g.Holds ? "holds" : "fails")}",
                Meaning.Act a => $"{a.Machine} trigger {a.Trigger}",
                Meaning.Happens h => $"{h.Machine} trigger {h.Trigger}",
                Meaning.Outcome o =>
                    $"{o.Machine} outcome {o.State} {glossary.Machines[o.Machine].Invokes[o.State].Outcomes[o.Phrase].Kind}",
                Meaning.ViewShows v => $"view {v.View} {v.Field}",
                _ => null,
            };
            if (part is not null && uses.TryGetValue(part, out var set))
                set.Add((scenario.File, step.Line));
        }

        return (
            phrases.Select(kv => new PhraseUse(kv.Key, kv.Value, uses[kv.Key].ToList())).ToList(),
            problems
        );
    }
}
