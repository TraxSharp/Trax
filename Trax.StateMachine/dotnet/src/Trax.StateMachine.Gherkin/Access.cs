using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Who may start, read and fire each machine, derived from who the features show doing it (ADR
/// <c>Trax.Docs/adr/0058</c>), and the table every grant is reported in (<c>access.txt</c>). With strict ownership
/// (ADR 0058) only a machine's owner acts on an instance, so a trigger is granted to the owning actor when a
/// scenario shows that actor's person doing it, fired only by the server when only steps with no person fire it, and
/// otherwise not served. Nothing is widened: a clash is a problem naming both scenarios.
/// </summary>
internal sealed class Access
{
    private Access(Dictionary<string, JsonObject> entries, JsonObject views, string table)
    {
        Entries = entries;
        Views = views;
        Table = table;
    }

    /// <summary>
    /// The views a scenario reads, as the host file serves them: what each reads, the fields it shows, and its readers,
    /// the actors whose people read it. A view no scenario reads is not served.
    /// </summary>
    internal JsonObject Views { get; }

    /// <summary>Each machine's <c>start</c>, <c>read</c> and <c>triggers</c> entries of the host file, by machine.</summary>
    internal IReadOnlyDictionary<string, JsonObject> Entries { get; }

    /// <summary>
    /// The report: one sorted line per start, read and trigger, with the actors its rule admits. An actor the rule
    /// admits that no scenario shows is marked, so a grant nobody meant is seen in review.
    /// </summary>
    internal string Table { get; }

    internal static Access? Derive(
        Glossary glossary,
        IReadOnlyList<Walk> walks,
        IReadOnlyDictionary<string, JsonObject> irs,
        List<Problem> problems
    )
    {
        var before = problems.Count;
        var entries = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var lines = new List<string>();
        var observations = walks.SelectMany(w => w.Observations).ToList();
        var starts = walks.SelectMany(w => w.Starts).ToList();

        foreach (var machine in glossary.Machines.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            var entry = new JsonObject();
            entries[machine.Id] = entry;
            var owner = machine.OwnedBy is { } o ? glossary.Actors[o] : null;
            var rule = owner is null ? null : Rule(owner);
            var triggers = new JsonObject();

            if (owner is not null && starts.Any(s => s.Machine == machine.Id))
            {
                entry["start"] = rule!.DeepClone();
                lines.Add(Line(machine, "start", rule!, owner, glossary));
            }
            if (owner is not null)
            {
                // An owner reads their own instances; the read rule is the owner's actor, which the host narrows to
                // owner-only (and discard follows it, ADR 0058).
                entry["read"] = rule!.DeepClone();
                lines.Add(Line(machine, "read", rule!, owner, glossary));
            }

            foreach (var trigger in machine.Triggers.Keys.Order(StringComparer.Ordinal))
            {
                var fired = observations
                    .Where(x => x.Machine == machine.Id && x.Trigger == trigger)
                    .ToList();
                var byPerson = fired.Where(x => x.Person is not null).ToList();
                var byServer = fired.Where(x => x.Person is null).ToList();
                var allowed = byPerson.Where(x => x.Result is not Result.Denied).ToList();
                var denied = byPerson.Where(x => x.Result is Result.Denied).ToList();

                // A trigger the server fires is never served to a client, so a person firing it too is two triggers
                // (ADR 0058).
                if (byPerson.Count > 0 && byServer.Count > 0)
                {
                    problems.Add(
                        new Problem(
                            byPerson[0].Scenario.File,
                            byPerson[0].Line,
                            $"{machine.Id}.{trigger} is fired by {byPerson[0].Person} here and by no one in '{byServer[0].Scenario.Name}' ({byServer[0].Scenario.Id}). A trigger the server fires is not a client's: make them two triggers."
                        )
                    );
                    continue;
                }
                if (allowed.Count > 0 && denied.Count > 0)
                {
                    problems.Add(
                        new Problem(
                            denied[0].Scenario.File,
                            denied[0].Line,
                            $"{denied[0].Person} may not {trigger} here, and {allowed[0].Person}, also {glossary.Actors[allowed[0].Actor!].Noun}, does in '{allowed[0].Scenario.Name}' ({allowed[0].Scenario.Id}). An actor may fire a trigger or not; say which."
                        )
                    );
                    continue;
                }

                if (allowed.Count > 0)
                {
                    // A gated event may not enter the start state: anyone who may start an instance can put one there,
                    // so the rule would gate nothing. The host refuses it; derivation says so first, per edge.
                    if (!owner!.AnyAuthenticated)
                        foreach (var edge in irs[machine.Id]["transitions"]!.AsArray())
                            if (
                                edge!["trigger"]!.GetValue<string>() == trigger
                                && edge["to"]!.GetValue<string>() == machine.Initial
                                && edge["from"]!.GetValue<string>() != machine.Initial
                            )
                                problems.Add(
                                    new Problem(
                                        allowed[0].Scenario.File,
                                        allowed[0].Line,
                                        $"{trigger} from {edge["from"]} goes back to {machine.Initial}, where the {machine.Reference} starts, and only {owner.Noun} may fire it: the rule would gate nothing, since whoever may start one can put it there. Send it to another state, or let any signed-in owner fire it."
                                    )
                                );
                    triggers[trigger] = rule!.DeepClone();
                    lines.Add(Line(machine, trigger, rule!, owner, glossary));
                }
                else if (byServer.Count > 0)
                {
                    triggers[trigger] = new JsonObject { ["server"] = true };
                    lines.Add($"{machine.Id} {trigger}: the server only");
                }
                else if (denied.Count > 0)
                    lines.Add($"{machine.Id} {trigger}: nobody");
            }
            if (triggers.Count > 0)
                entry["triggers"] = triggers;
        }

        var views = new JsonObject();
        foreach (
            var group in walks
                .SelectMany(w => w.Views)
                .GroupBy(v => v.View)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            var view = glossary.Views[group.Key];
            var readers = group
                .Select(v => glossary.Actors[v.Actor])
                .DistinctBy(a => a.Name)
                .OrderBy(a => a.Name, StringComparer.Ordinal)
                .ToList();
            var rule = readers.Any(a => a.AnyAuthenticated)
                ? new JsonObject { ["anyAuthenticated"] = true }
                : new JsonObject
                {
                    ["roles"] = new JsonArray(
                        new JsonArray(
                            readers
                                .SelectMany(a => a.Roles)
                                .Distinct()
                                .Order(StringComparer.Ordinal)
                                .Select(r => (JsonNode?)r)
                                .ToArray()
                        )
                    ),
                };
            var reads = new JsonArray();
            foreach (var read in view.Reads)
                reads.Add(
                    read.Key is { } key
                        ? new JsonObject { ["machine"] = read.Machine, ["key"] = key }
                        : new JsonObject { ["machine"] = read.Machine }
                );
            var fields = new JsonObject();
            foreach (var field in view.Fields)
            {
                var shown = new JsonObject { ["from"] = $"{field.Machine}.{field.Source}" };
                if (field.Labels is { } labels)
                {
                    var written = new JsonObject();
                    foreach (
                        var (state, label) in labels.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    )
                        written[state] = label;
                    shown["labels"] = written;
                }
                fields[field.Name] = shown;
            }
            views[group.Key] = new JsonObject
            {
                ["reads"] = reads,
                ["fields"] = fields,
                ["readers"] = rule,
            };
            var others = glossary
                .Actors.Values.Where(a => readers.All(r => r.Name != a.Name) && Admits(rule, a))
                .Select(a => $"{a.Name} (no scenario)");
            lines.Add(
                $"view {group.Key}: {string.Join(", ", readers.Select(r => r.Name).Concat(others.Order(StringComparer.Ordinal)))}"
            );
        }

        if (problems.Count > before)
            return null;
        var table = new StringBuilder();
        foreach (var line in lines.Order(StringComparer.Ordinal))
            table.Append(line).Append('\n');
        return new Access(entries, views, table.ToString());
    }

    // The owner's actor as a rule: any one of its roles, or any signed-in caller.
    private static JsonObject Rule(GlossaryActor actor) =>
        actor.AnyAuthenticated
            ? new JsonObject { ["anyAuthenticated"] = true }
            : new JsonObject
            {
                ["roles"] = new JsonArray(
                    new JsonArray(actor.Roles.Select(r => (JsonNode?)r).ToArray())
                ),
            };

    // "order Place: customer", and every other actor the rule admits, marked as no scenario's.
    private static string Line(
        GlossaryMachine machine,
        string what,
        JsonObject rule,
        GlossaryActor owner,
        Glossary glossary
    )
    {
        var admitted = glossary
            .Actors.Values.Where(a => a.Name != owner.Name && Admits(rule, a))
            .Select(a => $"{a.Name} (no scenario)")
            .Order(StringComparer.Ordinal);
        return $"{machine.Id} {what}: {string.Join(", ", new[] { owner.Name }.Concat(admitted))}";
    }

    private static bool Admits(JsonObject rule, GlossaryActor actor) =>
        rule.ContainsKey("anyAuthenticated")
        || (
            !actor.AnyAuthenticated
            && rule["roles"]![0]!
                .AsArray()
                .Select(r => r!.GetValue<string>())
                .Intersect(actor.Roles, StringComparer.Ordinal)
                .Any()
        );
}

/// <summary>
/// The rules each timer acts under, written into the IR as the exporter writes them (<c>BuiltMachine.TimerRules</c>):
/// the rules of everything that enters the timed state, widened through timers and invoked trains' outcomes until
/// nothing changes. A host serving the IR rebuilds the machine and refuses it if these differ from its access.
/// </summary>
internal static class TimerRules
{
    internal static void Fill(JsonObject ir, JsonObject access)
    {
        if (ir["timers"] is not JsonArray timers || timers.Count == 0)
            return;
        var entered = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        SortedSet<string> Of(string state) =>
            entered.TryGetValue(state, out var set)
                ? set
                : entered[state] = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var edge in ir["transitions"]!.AsArray())
            Of(edge!["to"]!.GetValue<string>())
                .Add(Text(access["triggers"]?[edge["trigger"]!.GetValue<string>()]));
        if (access["start"] is { } start)
            Of(ir["initialState"]!.GetValue<string>()).Add(Text(start));

        var server = timers
            .Select(t =>
                (From: t!["state"]!.GetValue<string>(), To: t["target"]!.GetValue<string>())
            )
            .Concat(
                (ir["outcomes"] as JsonObject ?? []).SelectMany(o =>
                    o.Value!["edges"]!
                        .AsArray()
                        .Select(e =>
                            (
                                From: o.Value["state"]!.GetValue<string>(),
                                To: e!["to"]!.GetValue<string>()
                            )
                        )
                )
            )
            .ToList();
        bool changed;
        do
        {
            changed = false;
            foreach (var (from, to) in server)
                if (entered.TryGetValue(from, out var source))
                    foreach (var rule in source.ToList())
                        changed |= Of(to).Add(rule);
        } while (changed);

        foreach (var timer in timers)
        {
            var state = timer!["state"]!.GetValue<string>();
            timer["rules"] = new JsonArray(
                (entered.TryGetValue(state, out var rules) ? rules : [])
                    .Select(r => (JsonNode?)r)
                    .ToArray()
            );
        }
    }

    // A rule as the census writes it: Requires(x), Requires(one of a, b), AnyAuthenticated, or undeclared.
    private static string Text(JsonNode? rule) =>
        rule switch
        {
            JsonObject r when r.ContainsKey("anyAuthenticated") => "AnyAuthenticated",
            JsonObject r when r["roles"] is JsonArray sets => string.Join(
                " and ",
                sets.Select(set =>
                    set!.AsArray() is { Count: 1 } one
                        ? $"Requires({one[0]})"
                        : $"Requires(one of {string.Join(", ", set.AsArray().Select(x => x!.GetValue<string>()))})"
                )
            ),
            _ => "undeclared",
        };
}
