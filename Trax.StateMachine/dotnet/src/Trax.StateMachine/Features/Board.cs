using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// The event-storming board of a machines directory (ADR <c>Trax.Docs/adr/0058</c>), read from the IRs and the host
/// file alone, so the CLI draws it from the committed files and a host from what it serves. Each sticky has the flow
/// role event storming gives it, and the scenarios that produced it when the host file has provenance. Stickies and
/// links are sorted, so the board of the same files is the same.
/// </summary>
internal static class Board
{
    /// <summary>A sticky's flow role: who acts, what they ask, what happened, the rule, the thing changed, the view.</summary>
    internal enum Kind
    {
        Actor,
        Command,
        Event,
        Policy,
        Aggregate,
        View,
    }

    internal sealed record Sticky(
        string Id,
        Kind Kind,
        string Machine,
        string Label,
        IReadOnlyList<Provenance> Scenarios
    );

    internal sealed record Link(string From, string To);

    internal sealed record Drawn(IReadOnlyList<Sticky> Stickies, IReadOnlyList<Link> Links);

    internal static Drawn Draw(IReadOnlyDictionary<string, JsonObject> irs, HostFile host)
    {
        var stickies = new Dictionary<string, Sticky>(StringComparer.Ordinal);
        var links = new HashSet<Link>();
        IReadOnlyList<Provenance> Of(string machine, string part) =>
            host.Provenance.GetValueOrDefault(machine)?.GetValueOrDefault(part) ?? [];
        string Add(Kind kind, string machine, string id, string label, string? part = null)
        {
            if (!stickies.ContainsKey(id))
                stickies[id] = new Sticky(
                    id,
                    kind,
                    machine,
                    label,
                    part is null ? [] : Of(machine, part)
                );
            return id;
        }

        foreach (var (id, ir) in irs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var entry = host.Machines[id];
            var aggregate = Add(
                Kind.Aggregate,
                id,
                $"aggregate:{id}",
                entry.SystemKey is { } key ? $"{id} ({key})"
                    : entry.KeyedBy is { } by ? $"{id} (by {string.Join(", ", by)})"
                    : id,
                "start"
            );
            if (entry.Start is { } start)
                links.Add(
                    new Link(Add(Kind.Actor, id, $"actor:{Who(start)}", Who(start)), aggregate)
                );

            foreach (var edge in ir["transitions"]?.AsArray() ?? [])
            {
                var from = edge!["from"]!.GetValue<string>();
                var trigger = edge["trigger"]!.GetValue<string>();
                var to = edge["to"]!.GetValue<string>();
                var command = Add(
                    Kind.Command,
                    id,
                    $"command:{id}:{trigger}",
                    trigger,
                    $"command {trigger}"
                );
                if (entry.Triggers.GetValueOrDefault(trigger) is { Client: { } rule })
                    links.Add(
                        new Link(Add(Kind.Actor, id, $"actor:{Who(rule)}", Who(rule)), command)
                    );
                var happened = Add(
                    Kind.Event,
                    id,
                    $"event:{id}:{from}:{trigger}:{to}",
                    $"{from} → {to}",
                    $"edge {from} {trigger} {to}"
                );
                links.Add(new Link(command, happened));
                links.Add(new Link(happened, aggregate));
                if (edge["guard"] is not null)
                    links.Add(
                        new Link(
                            Add(
                                Kind.Policy,
                                id,
                                $"guard:{id}:{from}:{trigger}",
                                $"only if allowed: {trigger} from {from}",
                                $"guard {from} {trigger}"
                            ),
                            command
                        )
                    );
            }

            foreach (var timer in ir["timers"]?.AsArray() ?? [])
            {
                var state = timer!["state"]!.GetValue<string>();
                var to = timer["target"]!.GetValue<string>();
                var after =
                    timer["after"]?.GetValue<string>() ?? $"{timer["field"]!["name"]} later";
                var rule = Add(
                    Kind.Policy,
                    id,
                    $"timer:{id}:{state}",
                    $"after {after} in {state}",
                    $"timer {state}"
                );
                var happened = Add(
                    Kind.Event,
                    id,
                    $"event:{id}:{state}:after:{to}",
                    $"{state} → {to}",
                    $"timer {state}"
                );
                links.Add(new Link(rule, happened));
                links.Add(new Link(happened, aggregate));
            }

            // The IR keys outcomes by "<state>.<kind>".
            foreach (var (_, outcome) in ir["outcomes"] as JsonObject ?? [])
            {
                var state = outcome!["state"]!.GetValue<string>();
                var kind = outcome["outcome"]!.GetValue<string>();
                foreach (var edge in outcome["edges"]!.AsArray())
                {
                    var to = edge!["to"]!.GetValue<string>();
                    var happened = Add(
                        Kind.Event,
                        id,
                        $"event:{id}:{state}:{kind}:{to}",
                        $"{state} {kind} → {to}",
                        $"outcome {state} {kind}"
                    );
                    links.Add(new Link(happened, aggregate));
                }
            }
        }

        foreach (var policy in host.Policies)
        {
            var rule = Add(
                Kind.Policy,
                policy.Target,
                $"policy:{policy.When}:{policy.Enters}:{policy.Target}:{policy.Trigger}",
                $"when {policy.When} is {policy.Enters}, {policy.Trigger} {policy.Target}",
                $"policy {policy.When} {policy.Enters}"
            );
            foreach (
                var cause in stickies
                    .Values.Where(s =>
                        s.Kind == Kind.Event
                        && s.Machine == policy.When
                        && s.Label.EndsWith($"→ {policy.Enters}", StringComparison.Ordinal)
                    )
                    .ToList()
            )
                links.Add(new Link(cause.Id, rule));
            links.Add(new Link(rule, $"command:{policy.Target}:{policy.Trigger}"));
        }

        foreach (var view in host.Views.Values)
        {
            var shown = Add(
                Kind.View,
                view.Reads[0].Machine,
                $"view:{view.Name}",
                view.Name,
                $"view {view.Name}"
            );
            foreach (var read in view.Reads)
                links.Add(new Link($"aggregate:{read.Machine}", shown));
            links.Add(
                new Link(
                    shown,
                    Add(
                        Kind.Actor,
                        view.Reads[0].Machine,
                        $"actor:{Who(view.Readers)}",
                        Who(view.Readers)
                    )
                )
            );
        }

        return new Drawn(
            stickies
                .Values.OrderBy(s => s.Machine, StringComparer.Ordinal)
                .ThenBy(s => s.Kind)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .ToList(),
            links
                .Where(l => stickies.ContainsKey(l.From) && stickies.ContainsKey(l.To))
                .OrderBy(l => l.From, StringComparer.Ordinal)
                .ThenBy(l => l.To, StringComparer.Ordinal)
                .ToList()
        );
    }

    // An actor sticky names who a rule admits: its roles, or anyone signed in.
    private static string Who(AccessRule rule) =>
        rule.IsAnyAuthenticated
            ? "anyone signed in"
            : string.Join(" and ", rule.RoleSets.Select(set => string.Join(" or ", set)));
}
