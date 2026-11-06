using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Who may start, read and fire each machine, derived from who the features show doing it (ADR
/// <c>Trax.Docs/adr/0058</c>), and the table every grant is reported in (<c>access.txt</c>). With strict ownership
/// (MS-18 decision 1) only a machine's owner acts on an instance, so a trigger is granted to the owning actor when a
/// scenario shows that actor's person doing it, fired only by the server when only steps with no person fire it, and
/// otherwise not served. Nothing is widened: a clash is a problem naming both scenarios.
/// </summary>
internal sealed class Access
{
    private Access(Dictionary<string, JsonObject> entries, string table)
    {
        Entries = entries;
        Table = table;
    }

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
                // owner-only (and discard follows it, MS-18 decision 15).
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
                // (MS-18 decision 3).
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

        if (problems.Count > before)
            return null;
        var table = new StringBuilder();
        foreach (var line in lines.Order(StringComparer.Ordinal))
            table.Append(line).Append('\n');
        return new Access(entries, table.ToString());
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
