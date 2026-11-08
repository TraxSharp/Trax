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
    /// What keeps the host file and the IRs from being a pair: a machine of one missing from the other, or a trigger,
    /// state or field a rule, an invoke, a policy or a view names that its IR does not have. Empty when they are one.
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
                foreach (var from in invoke.Input.Values.Where(f => !names.Fields.Contains(f)))
                    problems.Add(
                        $"{HostFileName} maps '{id}''s train input from '{from}', which no context of '{id}' has."
                    );
            }
        }

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
        return problems;
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
