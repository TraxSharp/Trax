using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// The server-only half of a set of machines derived from features (<c>conformance/host.schema.json</c>, version 1;
/// ADR <c>Trax.Docs/adr/0058</c>): each machine's owner, who may start, fire and read it, the train each invoking state
/// runs, the policies between machines, the views and the provenance. <see cref="Read"/> accepts exactly what the
/// schema accepts, then refuses a file whose names do not agree with each other. Whether they agree with the IRs is
/// checked where the two are paired, at registration. Every refusal is a <see cref="FormatException"/> naming the key.
/// </summary>
internal sealed record HostFile(
    IReadOnlyDictionary<string, HostMachine> Machines,
    IReadOnlyList<HostPolicy> Policies,
    IReadOnlyDictionary<string, HostView> Views,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Provenance>>> Provenance
)
{
    /// <summary>The only version this reader reads.</summary>
    internal const int Version = 1;

    private const string Document = "host.json";

    /// <summary>Reads a host file from its JSON text.</summary>
    /// <exception cref="FormatException">The text is not a host file this reader accepts; the message names the key.</exception>
    internal static HostFile Read(string json)
    {
        var at = new At(Document, "");
        var root = At.Parse(json, Document);
        at.Closed(root, "version", "machines", "policies", "views", "provenance");
        if (at["version"].Number(at.Required(root, "version")) != Version)
            throw at["version"].Fail($"must be {Version}.");

        var machinesAt = at["machines"];
        var machines = new Dictionary<string, HostMachine>(StringComparer.Ordinal);
        var given = machinesAt.Object(at.Required(root, "machines"));
        if (given.Count == 0)
            throw machinesAt.Fail("must hold at least 1.");
        foreach (var (id, node) in given)
        {
            Names.MachineIdAt(machinesAt[id], JsonValue.Create(id));
            machines[id] = ReadMachine(machinesAt[id], id, machinesAt[id].Object(node));
        }

        var policies = new List<HostPolicy>();
        if (root.ContainsKey("policies"))
        {
            var list = at["policies"].Array(root["policies"]);
            for (var i = 0; i < list.Count; i++)
                policies.Add(ReadPolicy(at["policies"][i], list[i]));
        }

        var views = new Dictionary<string, HostView>(StringComparer.Ordinal);
        if (root.ContainsKey("views"))
            foreach (var (name, node) in at["views"].Object(root["views"]))
            {
                var v = at["views"][name];
                v.Text(JsonValue.Create(name), 1, 200);
                var view = v.Object(node);
                v.Closed(view, "reads", "fields", "readers");
                var (reads, fields) = Features.Views.Read(v, view);
                views[name] = new HostView(
                    name,
                    reads,
                    fields,
                    ReadRule(v["readers"], v.Required(view, "readers"))
                );
            }

        var provenance = new Dictionary<
            string,
            IReadOnlyDictionary<string, IReadOnlyList<Provenance>>
        >(StringComparer.Ordinal);
        if (root.ContainsKey("provenance"))
            foreach (var (machine, node) in at["provenance"].Object(root["provenance"]))
            {
                var m = at["provenance"][machine];
                Names.MachineIdAt(m, JsonValue.Create(machine));
                var parts = new Dictionary<string, IReadOnlyList<Provenance>>(
                    StringComparer.Ordinal
                );
                foreach (var (part, scenarios) in m.Object(node))
                {
                    m[part].Text(JsonValue.Create(part), 1, 400);
                    var list = m[part].Array(scenarios, minItems: 1);
                    parts[part] = list.Select((s, i) => ReadScenario(m[part][i], s)).ToList();
                }
                provenance[machine] = parts;
            }

        var file = new HostFile(machines, policies, views, provenance);
        file.Check(at);
        return file;
    }

    private static HostMachine ReadMachine(At at, string id, JsonObject machine)
    {
        at.Closed(machine, "schemaHash", "owner", "start", "read", "triggers", "invokes");
        var hash = at["schemaHash"].Text(at.Required(machine, "schemaHash"), 1, 128);

        string? systemKey;
        var owner = at.Required(machine, "owner");
        if (
            owner is JsonValue value
            && value.GetValueKind() == System.Text.Json.JsonValueKind.String
        )
        {
            if (value.GetValue<string>() != "user")
                throw at["owner"].Fail("must be \"user\" or { \"system\": key }.");
            systemKey = null;
        }
        else
        {
            var o = at["owner"].Object(owner);
            at["owner"].Closed(o, "system");
            systemKey = Names.SystemKeyAt(at["owner"]["system"], at["owner"].Required(o, "system"));
        }

        var start = machine.ContainsKey("start") ? ReadRule(at["start"], machine["start"]) : null;
        var read = machine.ContainsKey("read") ? ReadRule(at["read"], machine["read"]) : null;

        var triggers = new Dictionary<string, TriggerAccess>(StringComparer.Ordinal);
        if (machine.ContainsKey("triggers"))
            foreach (var (name, node) in at["triggers"].Object(machine["triggers"]))
            {
                var t = at["triggers"][name];
                Names.MemberAt(t, JsonValue.Create(name));
                var rule = t.Object(node);
                if (rule.ContainsKey("server"))
                {
                    t.Closed(rule, "server");
                    t["server"].True(rule["server"]);
                    triggers[name] = TriggerAccess.Server;
                }
                else
                    triggers[name] = new TriggerAccess(ReadRule(t, rule));
            }

        var invokes = new Dictionary<string, HostInvoke>(StringComparer.Ordinal);
        if (machine.ContainsKey("invokes"))
            foreach (var (state, node) in at["invokes"].Object(machine["invokes"]))
            {
                var v = at["invokes"][state];
                Names.MemberAt(v, JsonValue.Create(state));
                var invoke = v.Object(node);
                v.Closed(invoke, "train", "input");
                var input = new Dictionary<string, string>(StringComparer.Ordinal);
                if (invoke.ContainsKey("input"))
                    foreach (var (field, from) in v["input"].Object(invoke["input"]))
                    {
                        Names.FieldNameAt(v["input"][field], JsonValue.Create(field));
                        input[field] = Names.FieldNameAt(v["input"][field], from);
                    }
                invokes[state] = new HostInvoke(
                    state,
                    Names.TrainAt(v["train"], v.Required(invoke, "train")),
                    input
                );
            }

        return new HostMachine(id, hash, systemKey, start, read, triggers, invokes);
    }

    /// <summary>A client rule: role sets, each to be met by one of its roles, or any signed-in caller.</summary>
    private static AccessRule ReadRule(At at, JsonNode? node)
    {
        var rule = at.Object(node);
        var roles = rule.ContainsKey("roles");
        var any = rule.ContainsKey("anyAuthenticated");
        if (roles == any)
            throw at.Fail("must be { \"roles\": [[...]] } or { \"anyAuthenticated\": true }.");
        if (any)
        {
            at.Closed(rule, "anyAuthenticated");
            at["anyAuthenticated"].True(rule["anyAuthenticated"]);
            return AccessRule.AnyAuthenticated;
        }
        at.Closed(rule, "roles");
        var sets = at["roles"].Array(rule["roles"], minItems: 1);
        var read = new List<IReadOnlyList<string>>(sets.Count);
        for (var i = 0; i < sets.Count; i++)
            read.Add(at["roles"][i].Distinct(sets[i], 1, (a, n) => a.Text(n, 1, 128)));
        return new AccessRule(read);
    }

    private static HostPolicy ReadPolicy(At at, JsonNode? node)
    {
        var policy = at.Object(node);
        at.Closed(policy, "shape", "when", "then");
        var shape = at["shape"]
            .OneOf(at.Required(policy, "shape"), "linked", "broadcast", "toSystem");

        var w = at["when"];
        var when = w.Object(at.Required(policy, "when"));
        w.Closed(when, "machine", "enters");
        var source = Names.MachineIdAt(w["machine"], w.Required(when, "machine"));
        var enters = Names.MemberAt(w["enters"], w.Required(when, "enters"));

        var t = at["then"];
        var then = t.Object(at.Required(policy, "then"));
        var by = shape switch
        {
            "linked" => "link",
            "broadcast" => "inState",
            _ => "key",
        };
        t.Closed(then, "machine", by, "trigger");
        var target = Names.MachineIdAt(t["machine"], t.Required(then, "machine"));
        var trigger = Names.MemberAt(t["trigger"], t.Required(then, "trigger"));
        var how = shape switch
        {
            "linked" => Names.FieldNameAt(t["link"], t.Required(then, "link")),
            "broadcast" => Names.MemberAt(t["inState"], t.Required(then, "inState")),
            _ => Names.SystemKeyAt(t["key"], t.Required(then, "key")),
        };
        return new HostPolicy(shape, source, enters, target, trigger, how);
    }

    private static Provenance ReadScenario(At at, JsonNode? node)
    {
        var scenario = at.Object(node);
        at.Closed(scenario, "file", "line", "scenario");
        var line = at["line"].Integer(at.Required(scenario, "line"));
        if (line < 1)
            throw at["line"].Fail("must be 1 or more.");
        return new Provenance(
            at["file"].Text(at.Required(scenario, "file"), 1, 400),
            (long)line,
            at["scenario"].Text(at.Required(scenario, "scenario"), 0, 400)
        );
    }

    // The checks that need more than one key at a time.
    private void Check(At at)
    {
        foreach (var machine in Machines.Values)
        {
            var m = at["machines"][machine.Id];
            if (machine.SystemKey is not { } key)
                continue;
            if (!key.StartsWith(machine.Id + "/", StringComparison.Ordinal))
                throw m["owner"]
                    ["system"]
                    .Fail($"must start with the machine's id, '{machine.Id}/'.");
            // No client starts, reads or fires a system machine: the host starts it, views read it, the server
            // moves it.
            if (machine.Start is not null)
                throw m["start"]
                    .Fail("a system machine is started by the host, never by a client.");
            if (machine.Read is not null)
                throw m["read"].Fail("a system machine is read through a view, never directly.");
            foreach (var (name, access) in machine.Triggers)
                if (!access.IsServer)
                    throw m["triggers"]
                        [name]
                        .Fail("a system machine's triggers are fired by the server only.");
            foreach (var invoke in machine.Invokes.Values)
            foreach (var (field, from) in invoke.Input)
                if (from == HostInvoke.Owner)
                    throw m["invokes"]
                        [invoke.State]["input"][field]
                        .Fail($"a system machine has no owner, so nothing is {HostInvoke.Owner}.");
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < Policies.Count; i++)
        {
            var policy = Policies[i];
            var p = at["policies"][i];
            // Two policies that wait for the same entry and fire the same trigger are one fire, named once.
            if (!seen.TryAdd(policy.Id, i))
                throw p.Fail($"'{policy.Id}' is the policy at /policies/{seen[policy.Id]} again.");
            if (!Machines.TryGetValue(policy.When, out var source))
                throw p["when"]["machine"].Fail($"'{policy.When}' is not a machine here.");
            if (!Machines.TryGetValue(policy.Target, out var target))
                throw p["then"]["machine"].Fail($"'{policy.Target}' is not a machine here.");
            // A trigger a policy fires is the server's alone, never also a client's.
            if (!target.Triggers.TryGetValue(policy.Trigger, out var access))
                throw p["then"]
                    ["trigger"]
                    .Fail($"'{policy.Trigger}' is not a trigger '{target.Id}' serves.");
            if (!access.IsServer)
                throw p["then"]
                    ["trigger"]
                    .Fail(
                        $"'{policy.Trigger}' is a client's trigger; a policy fires a server-only one."
                    );
            switch (policy.Shape)
            {
                case "toSystem":
                    if (target.SystemKey is null)
                        throw p["then"]
                            ["machine"]
                            .Fail(
                                $"'{target.Id}' is a user's machine; toSystem acts on a system one."
                            );
                    if (policy.By != target.SystemKey)
                        throw p["then"]["key"].Fail($"'{target.Id}' is at '{target.SystemKey}'.");
                    break;
                case "broadcast":
                    if (source.SystemKey is null)
                        throw p["when"]
                            ["machine"]
                            .Fail("a broadcast starts from a system machine.");
                    if (target.SystemKey is not null)
                        throw p["then"]["machine"].Fail("a broadcast reaches users' machines.");
                    break;
                case "linked":
                    if (source.SystemKey is not null)
                        throw p["when"]
                            ["machine"]
                            .Fail("a linked policy goes from one user's machine to another.");
                    if (target.SystemKey is not null)
                        throw p["then"]
                            ["machine"]
                            .Fail("a linked policy goes from one user's machine to another.");
                    break;
            }
        }

        var systemKeys = Machines.ToDictionary(
            m => m.Key,
            m => m.Value.SystemKey,
            StringComparer.Ordinal
        );
        Features.Views.CheckNames(at["views"], Views.Values.Select(v => (v.Name, v.Reads)));
        foreach (var view in Views.Values)
            Features.Views.Check(at["views"][view.Name], view.Reads, view.Fields, systemKeys);

        foreach (var machine in Provenance.Keys)
            if (!Machines.ContainsKey(machine))
                throw at["provenance"][machine].Fail($"'{machine}' is not a machine here.");
    }
}

/// <summary>One machine of a host file. <see cref="SystemKey"/> is null for a machine its users own.</summary>
internal sealed record HostMachine(
    string Id,
    string SchemaHash,
    string? SystemKey,
    AccessRule? Start,
    AccessRule? Read,
    IReadOnlyDictionary<string, TriggerAccess> Triggers,
    IReadOnlyDictionary<string, HostInvoke> Invokes
);

/// <summary>
/// Who a client rule admits: every role set met by one of its roles, or, with no sets, any signed-in caller. The
/// same reading as <see cref="CallerRule"/>'s role sets.
/// </summary>
internal sealed record AccessRule(IReadOnlyList<IReadOnlyList<string>> RoleSets)
{
    internal static readonly AccessRule AnyAuthenticated = new([]);

    internal bool IsAnyAuthenticated => RoleSets.Count == 0;
}

/// <summary>A trigger's access: a client rule, or the server alone (a timer or a policy fires it).</summary>
internal sealed record TriggerAccess(AccessRule? Client)
{
    internal static readonly TriggerAccess Server = new((AccessRule?)null);

    internal bool IsServer => Client is null;
}

/// <summary>
/// The train a state invokes and its input: each train field from a context field, or from <see cref="Owner"/>.
/// </summary>
internal sealed record HostInvoke(
    string State,
    string Train,
    IReadOnlyDictionary<string, string> Input
)
{
    /// <summary>
    /// The source that gives a train field the instance owner's qualified principal id (its user key). It is the
    /// server's to set: no context holds it, so no client can write it. A system machine has no owner.
    /// </summary>
    internal const string Owner = "$owner";
}

/// <summary>
/// The server acting when <see cref="When"/> enters <see cref="Enters"/>: on <see cref="Target"/> by
/// <see cref="By"/>, which is the link field (linked), the state its instances wait in (broadcast) or the system key
/// (toSystem).
/// </summary>
internal sealed record HostPolicy(
    string Shape,
    string When,
    string Enters,
    string Target,
    string Trigger,
    string By
)
{
    /// <summary>The policy's identity: what it waits for and what it fires, <c>shop.Open&gt;order.Bake</c>.</summary>
    internal string Id => $"{When}.{Enters}>{Target}.{Trigger}";
}

internal sealed record HostView(
    string Name,
    IReadOnlyList<ViewRead> Reads,
    IReadOnlyList<ViewField> Fields,
    AccessRule Readers
);

/// <summary>A scenario that produced part of a machine, by file and line; never its steps.</summary>
internal sealed record Provenance(string File, long Line, string Scenario);
