using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Which scenarios produced each part of each machine (ADR <c>Trax.Docs/adr/0058</c>), for the host file's
/// <c>provenance</c>: a scenario is its file, line and name, never its steps. A part is named by what it is:
/// <c>start</c>, <c>command Place</c>, <c>edge Draft Place Placed</c>, <c>guard Draft Place</c>, <c>timer Baking</c>,
/// <c>outcome Baking Ready</c>, <c>policy order Paid</c> (on the machine the policy moves) and <c>view orders</c> (on
/// each machine the view reads). Parts and scenarios are sorted, so the file does not depend on file names' order.
/// </summary>
internal static class Provenance
{
    // The host schema's bound on a scenario's file and name.
    private const int MaxText = 400;

    internal static JsonObject Derive(
        Glossary glossary,
        IReadOnlyList<Walk> walks,
        IReadOnlyCollection<string> machines,
        JsonObject views
    )
    {
        var parts = new SortedDictionary<string, SortedDictionary<string, SortedSet<Scenario>>>(
            StringComparer.Ordinal
        );
        void Add(string machine, string part, FeatureScenario scenario)
        {
            if (!machines.Contains(machine))
                return;
            if (!parts.TryGetValue(machine, out var byPart))
                parts[machine] = byPart = new(StringComparer.Ordinal);
            if (!byPart.TryGetValue(part, out var scenarios))
                byPart[part] = scenarios = [];
            scenarios.Add(new Scenario(scenario.File, scenario.Line, scenario.Name));
        }

        foreach (var walk in walks)
        {
            foreach (var start in walk.Starts)
                Add(start.Machine, "start", start.Scenario);
            foreach (var o in walk.Observations)
            {
                Add(o.Machine, $"command {o.Trigger}", o.Scenario);
                switch (o.Result)
                {
                    case Result.Moved moved:
                        Add(o.Machine, $"edge {o.From} {o.Trigger} {moved.To}", o.Scenario);
                        break;
                    case Result.Refused:
                        Add(o.Machine, $"guard {o.From} {o.Trigger}", o.Scenario);
                        break;
                }
            }
            foreach (var t in walk.Timers)
                Add(t.Machine, $"timer {t.From}", t.Scenario);
            foreach (var s in walk.Stays)
                Add(s.Machine, $"timer {s.State}", s.Scenario);
            foreach (var o in walk.Outcomes)
                Add(o.Machine, $"outcome {o.State} {o.To}", o.Scenario);
            foreach (var p in walk.Policies)
                Add(p.Target, $"policy {p.When} {p.Enters}", p.Scenario);
            foreach (var v in walk.Views)
                if (views.ContainsKey(v.View))
                    foreach (var read in glossary.Views[v.View].Reads)
                        Add(read.Machine, $"view {v.View}", v.Scenario);
        }

        var document = new JsonObject();
        foreach (var (machine, byPart) in parts)
        {
            var entry = new JsonObject();
            foreach (var (part, scenarios) in byPart)
                entry[part] = new JsonArray(
                    scenarios
                        .Select(s =>
                            (JsonNode)
                                new JsonObject
                                {
                                    ["file"] = Bounded(s.File),
                                    ["line"] = s.Line,
                                    ["scenario"] = Bounded(s.Name),
                                }
                        )
                        .ToArray()
                );
            document[machine] = entry;
        }
        return document;
    }

    private static string Bounded(string text) => text.Length <= MaxText ? text : text[..MaxText];

    private sealed record Scenario(string File, long Line, string Name) : IComparable<Scenario>
    {
        public int CompareTo(Scenario? other)
        {
            if (other is null)
                return 1;
            var byFile = string.CompareOrdinal(File, other.File);
            if (byFile != 0)
                return byFile;
            var byLine = Line.CompareTo(other.Line);
            return byLine != 0 ? byLine : string.CompareOrdinal(Name, other.Name);
        }
    }
}
