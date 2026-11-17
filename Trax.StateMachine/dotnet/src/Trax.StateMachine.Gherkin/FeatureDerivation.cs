using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Derives machines from features and a glossary (ADR <c>Trax.Docs/adr/0058</c>): resolve each step, walk each
/// scenario into observations, merge them per state and trigger into edges, write the IRs and the host file, and replay
/// every scenario on what was written. A derivation that its own features fail is a problem, never a machine.
/// </summary>
internal static class FeatureDerivation
{
    internal static async Task<Derived> DeriveAsync(
        Glossary glossary,
        IReadOnlyList<(string Path, string Text)> features
    )
    {
        var (scenarios, problems) = FeatureFiles.Parse(features);
        var resolver = new StepResolver(glossary);
        // A scenario that needs the host's own handlers says nothing derivation can check with the glossary's examples, so
        // it shapes no machine and is not replayed; its Givens and Whens must still read.
        foreach (var scenario in scenarios.Where(s => s.HostOnly))
            problems.AddRange(Unread(scenario, resolver));
        var walks = scenarios
            .Where(s => !s.HostOnly)
            .Select(s => Walk.Run(s, glossary, resolver))
            .ToList();
        foreach (var walk in walks)
            problems.AddRange(walk.Problems);
        var uiSteps = walks.SelectMany(w => w.UiSteps).ToList();

        var irs = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        // A machine no scenario uses is not derived, as a view no scenario reads is not served: its IR would serve
        // states nothing shows.
        var used = Used(walks);
        if (problems.Count == 0)
            foreach (var machine in glossary.Machines.Values.Where(m => used.Contains(m.Id)))
            {
                var ir = Merge.Machine(machine, walks, problems);
                if (ir is not null)
                    irs[machine.Id] = ir;
            }

        var warnings = new List<Problem>();
        var policies =
            problems.Count == 0 ? PolicyMerge.Derive(glossary, walks, irs, problems, warnings) : [];

        var access = problems.Count == 0 ? Access.Derive(glossary, walks, irs, problems) : null;
        if (problems.Count > 0 || access is null)
            return new Derived(
                new Dictionary<string, string>(StringComparer.Ordinal),
                Host: null,
                Access: null,
                Sorted(problems),
                Sorted(uiSteps),
                Sorted(warnings)
            );

        foreach (var (id, ir) in irs)
            TimerRules.Fill(ir, access.Entries[id]);
        var written = irs.ToDictionary(
            kv => kv.Key,
            kv => CanonicalJson.Serialize(kv.Value),
            StringComparer.Ordinal
        );
        var host = Host(glossary, walks, written, access, policies);

        // Every scenario is run on what was derived, access included, as the host would answer it. A derivation its
        // own features fail is a problem, never a machine.
        var hostFile = HostFile.Read(host);
        foreach (var walk in walks)
        {
            ScenarioResult run;
            try
            {
                run = await ScenarioRunner
                    .Run(walk, glossary, new MemoryDriver(irs, hostFile, glossary))
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // A replay that cannot run is a problem with this scenario, not a crash of the whole derivation.
                problems.Add(
                    new Problem(
                        walk.Scenario.File,
                        walk.Scenario.Line,
                        $"The derived machine could not be run on this scenario: {e.Message} (scenario '{walk.Scenario.Name}', {walk.Scenario.Id})"
                    )
                );
                continue;
            }
            if (run.Failure is { } failure)
                problems.Add(
                    new Problem(
                        walk.Scenario.File,
                        failure.Step.Line,
                        $"The derived machine answers this differently: {failure.Message} (scenario '{walk.Scenario.Name}', {walk.Scenario.Id})"
                    )
                );
        }
        if (problems.Count > 0)
            return new Derived(
                new Dictionary<string, string>(StringComparer.Ordinal),
                Host: null,
                Access: null,
                Sorted(problems),
                Sorted(uiSteps),
                Sorted(warnings)
            );

        return new Derived(written, host, access.Table, [], Sorted(uiSteps), Sorted(warnings));
    }

    // The server half of what the glossary names in C#: the effect each effect-bound edge of the IR runs, the type of each
    // custom rule and reduction the IR names, and each migration, one entry for each and nothing the IR does not use, as
    // the host pairs them.
    private static void Handlers(JsonObject entry, GlossaryMachine machine, JsonObject ir)
    {
        var effects = new JsonObject();
        foreach (
            var trigger in (ir["transitions"] as JsonArray ?? [])
                .Where(t => t?["effect"] is JsonObject)
                .Select(t => t.NotNull()["trigger"].NotNull().GetValue<string>())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        )
            effects[trigger] = machine.Triggers[trigger].Effect.NotNull().Type;
        if (effects.Count > 0)
            entry["effects"] = effects;

        var rules = new SortedSet<string>(StringComparer.Ordinal);
        var reductions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in ir)
            if (!string.Equals(key, "differential", StringComparison.Ordinal))
                Glossary.CustomNames(value, rules, reductions);
        var handlers = new JsonObject();
        if (rules.Count > 0)
            handlers["rules"] = new JsonObject(
                rules.Select(r => KeyValuePair.Create(r, (JsonNode?)machine.Rules[r].Type))
            );
        if (reductions.Count > 0)
            handlers["reductions"] = new JsonObject(
                reductions.Select(r =>
                    KeyValuePair.Create(r, (JsonNode?)machine.Reductions[r].Type)
                )
            );
        if (handlers.Count > 0)
            entry["handlers"] = handlers;

        if (machine.Migrations.Count > 0)
            entry["migrations"] = new JsonObject(
                machine.Migrations.Select(m =>
                    KeyValuePair.Create(
                        m.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        (JsonNode?)m.Value
                    )
                )
            );
    }

    // The Givens and Whens of a scenario no phrase reads, with the people it names as it goes.
    private static IEnumerable<Problem> Unread(FeatureScenario scenario, StepResolver resolver)
    {
        var people = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var step in scenario.Steps)
        {
            var meaning = resolver.Resolve(step.Text, people, out var problem, out var unresolved);
            if (meaning is Meaning.Named named)
                people[named.Name] = named.Actor;
            else if (meaning is null && !(unresolved && step.Kind == StepKind.Then))
                yield return new Problem(
                    scenario.File,
                    step.Line,
                    $"{problem} (scenario '{scenario.Name}', {scenario.Id})"
                );
        }
    }

    // The host file: its machines, then its policies, views and provenance when it has any.
    private static JsonObject Document(
        JsonObject machines,
        JsonArray policies,
        JsonObject views,
        JsonObject provenance
    )
    {
        var document = new JsonObject { ["version"] = HostFile.Version, ["machines"] = machines };
        if (policies.Count > 0)
            document["policies"] = policies.DeepClone();
        if (views.Count > 0)
            document["views"] = views.DeepClone();
        if (provenance.Count > 0)
            document["provenance"] = provenance;
        return document;
    }

    private static HashSet<string> Used(IEnumerable<Walk> walks)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var walk in walks)
        {
            used.UnionWith(walk.Observations.Select(o => o.Machine));
            used.UnionWith(walk.Starts.Select(s => s.Machine));
            used.UnionWith(walk.Policies.SelectMany(p => new[] { p.When, p.Target }));
            foreach (var (_, meaning) in walk.Steps)
                if (
                    meaning switch
                    {
                        Meaning.Start m => m.Machine,
                        Meaning.Act m => m.Machine,
                        Meaning.Happens m => m.Machine,
                        Meaning.InState m => m.Machine,
                        Meaning.Guard m => m.Machine,
                        Meaning.Outcome m => m.Machine,
                        _ => null,
                    } is
                    { } machine
                )
                    used.Add(machine);
        }
        return used;
    }

    private static List<Problem> Sorted(IEnumerable<Problem> problems) =>
        problems
            .Distinct()
            .OrderBy(p => p.File, StringComparer.Ordinal)
            .ThenBy(p => p.Line)
            .ThenBy(p => p.Message, StringComparer.Ordinal)
            .ToList();

    // The server-only half: each machine's owner, the IR it was derived with, and the train each invoking state runs.
    // Who may start, read and fire each machine is the access derivation's.
    private static string Host(
        Glossary glossary,
        IReadOnlyList<Walk> walks,
        IReadOnlyDictionary<string, string> irs,
        Access access,
        JsonArray policies
    )
    {
        var machines = new JsonObject();
        foreach (var (id, ir) in irs)
        {
            var machine = glossary.Machines[id];
            var entry = new JsonObject
            {
                ["schemaHash"] = Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(ir))
                ),
                ["owner"] =
                    machine.SystemKey is { } key ? new JsonObject { ["system"] = key }
                    : machine.KeyedBy is { } keyedBy
                        ? new JsonObject
                        {
                            ["system"] = new JsonObject
                            {
                                ["keyedBy"] = new JsonArray(
                                    keyedBy.Select(p => (JsonNode?)p).ToArray()
                                ),
                            },
                        }
                    : "user",
            };
            foreach (var (name, value) in access.Entries[id])
                entry[name] = value.NotNull().DeepClone();
            if (machine.Invokes.Count > 0)
            {
                var invokes = new JsonObject();
                foreach (
                    var invoke in machine.Invokes.Values.OrderBy(
                        i => i.State,
                        StringComparer.Ordinal
                    )
                )
                {
                    var input = new JsonObject();
                    foreach (
                        var (field, from) in invoke.Input.OrderBy(
                            kv => kv.Key,
                            StringComparer.Ordinal
                        )
                    )
                        input[field] = from;
                    invokes[invoke.State] = new JsonObject
                    {
                        ["train"] = invoke.Train,
                        ["input"] = input,
                    };
                }
                entry["invokes"] = invokes;
            }
            Handlers(entry, machine, JsonNode.Parse(ir).NotNull().AsObject());
            machines[id] = entry;
        }
        return CanonicalJson.Serialize(
            Document(
                machines,
                policies,
                access.Views,
                Provenance.Derive(glossary, walks, irs.Keys.ToList(), access.Views)
            )
        );
    }
}
