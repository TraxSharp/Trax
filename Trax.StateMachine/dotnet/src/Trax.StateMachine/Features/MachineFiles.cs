using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// The files machines derived from features are written as (ADR <c>Trax.Docs/adr/0058</c>): one <c>&lt;id&gt;.ir.json</c>
/// per machine and one <c>host.json</c>, and what makes them a pair. A host serves them, and a feature run and the board
/// read them, only when the host file and the IRs list the same machines, every name the host file gives is in its
/// IR, and each IR is the one its host entry was derived from.
/// </summary>
internal static class MachineFiles
{
    internal const string HostFileName = "host.json";
    internal const string IrSuffix = ".ir.json";

    /// <summary>The hash a host entry names its IR by: the canonical IR's, which the store writes to every instance.</summary>
    internal static string Hash(JsonObject ir)
    {
        var copy = (JsonObject)ir.DeepClone();
        copy.Remove("irFormat");
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(copy)))
        );
    }

    /// <summary>
    /// What keeps the host file and the IRs from being a pair: a machine of one missing from the other, a trigger,
    /// state or field a rule, an invoke, a policy or a view names that its IR does not have, or policies that set each
    /// other off with no timer between them (<see cref="PolicyLoops"/>). Empty when they are one.
    /// The hashes are not compared here.
    /// </summary>
    internal static List<string> Problems(
        HostFile host,
        IReadOnlyDictionary<string, JsonObject> irs
    )
    {
        var problems = new List<string>();
        foreach (var id in host.Machines.Keys.Where(id => !irs.ContainsKey(id)))
            problems.Add($"{HostFileName} serves '{id}', and there is no {id}{IrSuffix}.");
        foreach (var id in irs.Keys.Where(id => !host.Machines.ContainsKey(id)))
            problems.Add(
                $"{id}{IrSuffix} has no entry in {HostFileName}, so nothing says who may use it."
            );
        if (problems.Count > 0)
            return problems;

        var machines = irs.ToDictionary(
            kv => kv.Key,
            kv => new IrNames(kv.Value),
            StringComparer.Ordinal
        );

        foreach (var (id, machine) in host.Machines)
        {
            var names = machines[id];
            foreach (var trigger in machine.Triggers.Keys.Where(t => !names.Triggers.Contains(t)))
                problems.Add(
                    $"{HostFileName} gives '{id}' a rule for '{trigger}', which is not one of its triggers."
                );
            foreach (var (state, invoke) in machine.Invokes)
            {
                if (!names.States.Contains(state))
                    problems.Add(
                        $"{HostFileName} names a train for '{id}' in '{state}', which is not one of its states."
                    );
                foreach (
                    var from in invoke.Input.Values.Where(f =>
                        f != HostInvoke.Owner && !names.Fields.Contains(f)
                    )
                )
                    problems.Add(
                        $"{HostFileName} maps '{id}''s train input from '{from}', which no context of '{id}' has."
                    );
                // $owner is the server's value; a context field under that name would be one a client writes.
                if (
                    invoke.Input.Values.Contains(HostInvoke.Owner)
                    && names.Fields.Contains(HostInvoke.Owner)
                )
                    problems.Add(
                        $"{id}{IrSuffix} has a context field named '{HostInvoke.Owner}', which {HostFileName} "
                            + "reads as the instance's owner."
                    );
            }
        }

        foreach (var (id, machine) in host.Machines)
            problems.AddRange(HandlerProblems(machine, irs[id]));

        for (var i = 0; i < host.Policies.Count; i++)
        {
            var policy = host.Policies[i];
            var at = $"{HostFileName} policy {i} ({policy.Shape})";
            if (!machines[policy.When].States.Contains(policy.Enters))
                problems.Add(
                    $"{at} waits for '{policy.When}' to enter '{policy.Enters}', which is not one of its states."
                );
            var target = machines[policy.Target];
            if (!target.Triggers.Contains(policy.Trigger))
                problems.Add(
                    $"{at} fires '{policy.Trigger}', which is not one of '{policy.Target}''s triggers."
                );
            if (policy.Shape == "broadcast" && !target.States.Contains(policy.By))
                problems.Add(
                    $"{at} reaches '{policy.Target}' in '{policy.By}', which is not one of its states."
                );
            if (policy.Shape == "linked" && !target.Fields.Contains(policy.By))
                problems.Add(
                    $"{at} links by '{policy.By}', which no context of '{policy.Target}' has."
                );
        }

        foreach (var view in host.Views.Values)
        foreach (var field in view.Fields)
        {
            var names = machines[field.Machine];
            if (!field.IsState && !names.Fields.Contains(field.Source))
                problems.Add(
                    $"{HostFileName} view '{view.Name}' shows '{field.Machine}.{field.Source}', which no context of '{field.Machine}' has."
                );
            foreach (var state in field.Labels?.Keys ?? [])
                if (!names.States.Contains(state))
                    problems.Add(
                        $"{HostFileName} view '{view.Name}' labels '{state}', which is not a state of '{field.Machine}'."
                    );
        }

        // Derivation refuses the same loops through the same check, so a hand-edited host file cannot add one.
        problems.AddRange(PolicyLoops.Find(host.Policies, irs).Select(PolicyLoops.Problem));
        return problems;
    }

    /// <summary>
    /// What keeps a host entry's handlers from pairing with its IR, one for one: every trigger whose edge runs an effect
    /// has its effect's type and nothing else does, every custom rule and reduction the IR names has its type and nothing
    /// else does, and every version step below the IR's has its migration and nothing else does. Empty when they pair.
    /// </summary>
    internal static List<string> HandlerProblems(HostMachine machine, JsonObject ir)
    {
        var problems = new List<string>();
        var id = machine.Id;
        var effects = (ir["transitions"] as JsonArray ?? [])
            .Where(t => t?["effect"] is JsonObject)
            .Select(t => t!["trigger"]?.GetValue<string>())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        Pair(problems, $"'{id}' runs an effect on", "effect", effects, machine.Effects.Keys);

        var rules = new SortedSet<string>(StringComparer.Ordinal);
        var reductions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in ir)
            if (key != "differential")
                Glossary.CustomNames(value, rules, reductions);
        Pair(problems, $"'{id}' names the custom rule", "rule", rules, machine.Rules.Keys);
        Pair(
            problems,
            $"'{id}' names the custom reduction",
            "reduction",
            reductions,
            machine.Reductions.Keys
        );

        var version = ir["version"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 1;
        var steps = Enumerable
            .Range(1, Math.Max(0, version - 1))
            .Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
        Pair(
            problems,
            $"'{id}' is version {version}, and steps from version",
            "migration",
            steps,
            machine.Migrations.Keys.Select(k =>
                k.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )
        );
        return problems;
    }

    // Each name the IR has needs its host entry, and an entry the IR does not have binds nothing, so it is refused too.
    private static void Pair(
        List<string> problems,
        string what,
        string kind,
        IReadOnlySet<string> needed,
        IEnumerable<string> given
    )
    {
        var have = given.ToHashSet(StringComparer.Ordinal);
        foreach (var name in needed.Order(StringComparer.Ordinal).Where(n => !have.Contains(n)))
            problems.Add($"{what} '{name}', and {HostFileName} names no {kind} type for it.");
        foreach (var name in have.Order(StringComparer.Ordinal).Where(n => !needed.Contains(n)))
            problems.Add(
                $"{HostFileName} names a {kind} type for '{name}', which {what.Split(' ')[0]}'s IR does not have."
            );
    }

    /// <summary>Why an IR is not the one its host entry was derived from, or null when it is.</summary>
    internal static string? Mismatch(HostFile host, string id, JsonObject ir) =>
        host.Machines[id].SchemaHash == Hash(ir)
            ? null
            : $"{HostFileName} was derived from another IR of '{id}'. Derive them again together.";

    // The names an IR declares: its states, triggers and every top-level field of any of its contexts.
    private sealed class IrNames(JsonObject ir)
    {
        public HashSet<string> States { get; } = Names(ir["states"]);
        public HashSet<string> Triggers { get; } = Names(ir["triggers"]);
        public HashSet<string> Fields { get; } =
            (ir["context"] as JsonObject ?? [])
                .SelectMany(c => c.Value?["fields"] as JsonArray ?? [])
                .Select(f => f?["name"]?.GetValue<string>())
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

        private static HashSet<string> Names(JsonNode? node) =>
            (node as JsonArray ?? [])
                .Select(n => n?.GetValue<string>())
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
    }
}
