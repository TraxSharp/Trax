using System.Text;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Who may start, read and fire each machine, derived from who the features show doing it (ADR
/// <c>Trax.Docs/adr/0058</c>), and the table every grant is reported in (<c>access.txt</c>). With strict ownership
/// (ADR 0058) only a machine's owner acts on an instance, so a trigger is granted to the owning actors whose people a
/// scenario shows doing it, fired only by the server when only steps with no person fire it, and otherwise not served.
/// Nothing is widened: a clash, or a rule whose roles would admit an actor a scenario denies, is a problem naming both
/// scenarios.
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
    /// The report: each actor's roles (<c>@customer: roles customer</c>), then one sorted line per start, read, trigger
    /// and view, with the actors its rule admits. An actor the rule admits that no scenario shows is marked, so a grant
    /// nobody meant is seen in review.
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
            DeriveMachine(machine, glossary, irs, observations, starts, entries, lines, problems);

        var views = new JsonObject();
        foreach (
            var group in walks
                .SelectMany(w => w.Views)
                .GroupBy(v => v.View, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
            DeriveView(group, glossary, irs, views, lines, problems);

        if (problems.Count > before)
            return null;
        // Each actor's roles first, so a role an actor gains or loses changes the report as a new grant would.
        foreach (var actor in glossary.Actors.Values)
            lines.Add(
                actor.AnyAuthenticated
                    ? $"@{actor.Name}: anyone signed in"
                    : $"@{actor.Name}: roles {string.Join(", ", actor.Roles.Order(StringComparer.Ordinal))}"
            );
        var table = new StringBuilder();
        foreach (var line in lines.Order(StringComparer.Ordinal))
            table.Append(line).Append('\n');
        return new Access(entries, views, table.ToString());
    }

    private static void DeriveMachine(
        GlossaryMachine machine,
        Glossary glossary,
        IReadOnlyDictionary<string, JsonObject> irs,
        List<Observation> observations,
        List<StartObservation> starts,
        Dictionary<string, JsonObject> entries,
        List<string> lines,
        List<Problem> problems
    )
    {
        var entry = new JsonObject();
        entries[machine.Id] = entry;
        var owners = machine.OwnedBy?.Select(o => glossary.Actors[o]).ToList();
        var triggers = new JsonObject();

        if (owners is not null)
        {
            // Start is granted to the owning actors whose people the features show starting one.
            var starters = owners
                .Where(a =>
                    starts.Exists(s =>
                        string.Equals(s.Machine, machine.Id, StringComparison.Ordinal)
                        && string.Equals(s.Actor, a.Name, StringComparison.Ordinal)
                    )
                )
                .ToList();
            if (starters.Count > 0)
            {
                var start = Rule(starters);
                entry["start"] = start;
                lines.Add(Line(machine, "start", start, starters, glossary));
            }
            // An owner reads their own instances; the read rule is the owning actors', which the host narrows to
            // owner-only (and discard follows it, ADR 0058).
            var read = Rule(owners);
            entry["read"] = read;
            lines.Add(Line(machine, "read", read, owners, glossary));
        }

        foreach (var trigger in machine.Triggers.Keys.Order(StringComparer.Ordinal))
            DeriveTrigger(
                machine,
                trigger,
                owners,
                glossary,
                irs,
                observations,
                triggers,
                lines,
                problems
            );
        if (triggers.Count > 0)
            entry["triggers"] = triggers;
    }

    private static void DeriveTrigger(
        GlossaryMachine machine,
        string trigger,
        List<GlossaryActor>? owners,
        Glossary glossary,
        IReadOnlyDictionary<string, JsonObject> irs,
        List<Observation> observations,
        JsonObject triggers,
        List<string> lines,
        List<Problem> problems
    )
    {
        var fired = observations
            .Where(x =>
                string.Equals(x.Machine, machine.Id, StringComparison.Ordinal)
                && string.Equals(x.Trigger, trigger, StringComparison.Ordinal)
            )
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
            return;
        }

        // Each trigger is granted the owning actors shown firing it, and only them.
        var granted =
            owners
                ?.Where(a =>
                    allowed.Exists(x => string.Equals(x.Actor, a.Name, StringComparison.Ordinal))
                )
                .ToList()
            ?? [];
        if (ReportedClash(trigger, granted, allowed, denied, glossary, problems))
            return;

        if (granted.Count > 0)
            GrantTrigger(
                machine,
                trigger,
                granted,
                allowed,
                denied,
                glossary,
                irs,
                triggers,
                lines,
                problems
            );
        else if (byServer.Count > 0)
        {
            triggers[trigger] = new JsonObject { ["server"] = true };
            lines.Add($"{machine.Id} {trigger}: the server only");
        }
        else if (denied.Count > 0)
            lines.Add($"{machine.Id} {trigger}: nobody");
    }

    // True when an actor a scenario denies the trigger is also granted it by another scenario: a problem.
    private static bool ReportedClash(
        string trigger,
        List<GlossaryActor> granted,
        List<Observation> allowed,
        List<Observation> denied,
        Glossary glossary,
        List<Problem> problems
    )
    {
        var clash = denied.FirstOrDefault(d =>
            granted.Exists(a => string.Equals(a.Name, d.Actor, StringComparison.Ordinal))
        );
        if (clash is null)
            return false;
        var also = allowed.First(x =>
            string.Equals(x.Actor, clash.Actor, StringComparison.Ordinal)
        );
        problems.Add(
            new Problem(
                clash.Scenario.File,
                clash.Line,
                $"{clash.Person} may not {trigger} here, and {also.Person}, also {glossary.Actors[also.Actor.NotNull()].Noun}, does in '{also.Scenario.Name}' ({also.Scenario.Id}). An actor may fire a trigger or not; say which."
            )
        );
        return true;
    }

    private static void GrantTrigger(
        GlossaryMachine machine,
        string trigger,
        List<GlossaryActor> granted,
        List<Observation> allowed,
        List<Observation> denied,
        Glossary glossary,
        IReadOnlyDictionary<string, JsonObject> irs,
        JsonObject triggers,
        List<string> lines,
        List<Problem> problems
    )
    {
        var rule = Rule(granted);
        // The rule is the granted actors' roles, so an actor a scenario denies must hold none of them: a
        // rule that admits them too is a widening, and a problem.
        var widened = denied.FirstOrDefault(d => Admits(rule, glossary.Actors[d.Actor.NotNull()]));
        if (widened is not null)
        {
            problems.Add(
                new Problem(
                    widened.Scenario.File,
                    widened.Line,
                    $"{widened.Person} may not {trigger} here, and the rule that lets {string.Join(" and ", granted.Select(a => a.Noun))} fire it in '{allowed[0].Scenario.Name}' ({allowed[0].Scenario.Id}) admits {glossary.Actors[widened.Actor.NotNull()].Noun} too, by its roles. Give the actors roles that tell them apart."
                )
            );
            return;
        }

        // A gated event may not enter the start state: anyone who may start an instance can put one there,
        // so the rule would gate nothing. The host refuses it; derivation says so first, per edge.
        if (!rule.ContainsKey("anyAuthenticated"))
            foreach (var edge in irs[machine.Id]["transitions"].NotNull().AsArray())
                if (
                    string.Equals(
                        edge.NotNull()["trigger"].NotNull().GetValue<string>(),
                        trigger,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        edge["to"].NotNull().GetValue<string>(),
                        machine.Initial,
                        StringComparison.Ordinal
                    )
                    && !string.Equals(
                        edge["from"].NotNull().GetValue<string>(),
                        machine.Initial,
                        StringComparison.Ordinal
                    )
                )
                    problems.Add(
                        new Problem(
                            allowed[0].Scenario.File,
                            allowed[0].Line,
                            $"{trigger} from {edge["from"]} goes back to {machine.Initial}, where the {machine.Reference} starts, and only {string.Join(" or ", granted.Select(a => a.Noun))} may fire it: the rule would gate nothing, since whoever may start one can put it there. Send it to another state, or let any signed-in owner fire it."
                        )
                    );
        triggers[trigger] = rule;
        lines.Add(Line(machine, trigger, rule, granted, glossary));
    }

    private static void DeriveView(
        IGrouping<string, ViewObservation> group,
        Glossary glossary,
        IReadOnlyDictionary<string, JsonObject> irs,
        JsonObject views,
        List<string> lines,
        List<Problem> problems
    )
    {
        var view = glossary.Views[group.Key];
        var unused = view.Reads.FirstOrDefault(r => !irs.ContainsKey(r.Machine));
        if (unused is not null)
        {
            var first = group
                .OrderBy(v => v.Scenario.File, StringComparer.Ordinal)
                .ThenBy(v => v.Line)
                .First();
            problems.Add(
                new Problem(
                    first.Scenario.File,
                    first.Line,
                    $"The view '{view.Name}' reads the machine '{unused.Machine}', which no scenario "
                        + "uses, so it is not derived. Show it in a scenario."
                )
            );
            return;
        }
        var readers = group
            .Select(v => glossary.Actors[v.Actor])
            .DistinctBy(a => a.Name, StringComparer.Ordinal)
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ToList();
        var rule = readers.Exists(a => a.AnyAuthenticated)
            ? new JsonObject { ["anyAuthenticated"] = true }
            : new JsonObject
            {
                ["roles"] = new JsonArray(
                    new JsonArray(
                        readers
                            .SelectMany(a => a.Roles)
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                            .Select(r => (JsonNode?)r)
                            .ToArray()
                    )
                ),
            };
        views[group.Key] = new JsonObject
        {
            ["reads"] = ViewReads(view),
            ["fields"] = ViewFields(view),
            ["readers"] = rule,
        };
        var others = glossary
            .Actors.Values.Where(a =>
                readers.TrueForAll(r => !string.Equals(r.Name, a.Name, StringComparison.Ordinal))
                && Admits(rule, a)
            )
            .Select(a => $"{a.Name} (no scenario)");
        lines.Add(
            $"view {group.Key}: {string.Join(", ", readers.Select(r => r.Name).Concat(others.Order(StringComparer.Ordinal)))}"
        );
    }

    private static JsonArray ViewReads(GlossaryView view)
    {
        var reads = new JsonArray();
        foreach (var read in view.Reads)
            reads.Add(
                read.Key is { } key
                    ? new JsonObject { ["machine"] = read.Machine, ["key"] = key }
                    : new JsonObject { ["machine"] = read.Machine }
            );
        return reads;
    }

    private static JsonObject ViewFields(GlossaryView view)
    {
        var fields = new JsonObject();
        foreach (var field in view.Fields)
        {
            var shown = new JsonObject { ["from"] = $"{field.Machine}.{field.Source}" };
            if (field.Labels is { } labels)
            {
                var written = new JsonObject();
                foreach (var (state, label) in labels.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    written[state] = label;
                shown["labels"] = written;
            }
            fields[field.Name] = shown;
        }
        return fields;
    }

    // The owning actors as a rule: any one of their roles, or any signed-in caller when one of them is.
    private static JsonObject Rule(IReadOnlyList<GlossaryActor> actors) =>
        actors.Any(a => a.AnyAuthenticated)
            ? new JsonObject { ["anyAuthenticated"] = true }
            : new JsonObject
            {
                ["roles"] = new JsonArray(
                    new JsonArray(
                        actors
                            .SelectMany(a => a.Roles)
                            .Distinct(StringComparer.Ordinal)
                            .Select(r => (JsonNode?)r)
                            .ToArray()
                    )
                ),
            };

    // "order Place: customer", and every other actor the rule admits, marked as no scenario's.
    private static string Line(
        GlossaryMachine machine,
        string what,
        JsonObject rule,
        IReadOnlyList<GlossaryActor> actors,
        Glossary glossary
    )
    {
        var named = actors.Select(a => a.Name).Order(StringComparer.Ordinal).ToList();
        var admitted = glossary
            .Actors.Values.Where(a => !named.Contains(a.Name) && Admits(rule, a))
            .Select(a => $"{a.Name} (no scenario)")
            .Order(StringComparer.Ordinal);
        return $"{machine.Id} {what}: {string.Join(", ", named.Concat(admitted))}";
    }

    private static bool Admits(JsonObject rule, GlossaryActor actor) =>
        rule.ContainsKey("anyAuthenticated")
        || (
            !actor.AnyAuthenticated
            && rule["roles"]
                .NotNull()[0]
                .NotNull()
                .AsArray()
                .Select(r => r.NotNull().GetValue<string>())
                .Intersect(actor.Roles, StringComparer.Ordinal)
                .Any()
        );
}
