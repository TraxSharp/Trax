using System.Text.Json.Nodes;
using System.Xml;

namespace Trax.Cli.Machines;

/// <summary>
/// What derivation read from each scenario (ADR <c>Trax.Docs/adr/0058</c>), turned round from the provenance
/// <c>host.json</c> records for every fact: per scenario, the edges, guards, outcomes, timers, policies, starts, grants
/// and views it gave, said in words. Derivation is the only reader; this is a view of its answer, never a second one.
/// </summary>
internal static class ScenarioExplanation
{
    /// <summary>One scenario and what it gave, each fact one line.</summary>
    internal sealed record Scenario(
        string File,
        int Line,
        string Name,
        IReadOnlyList<string> Facts
    );

    /// <summary>Every scenario some fact names, in file and line order.</summary>
    /// <param name="files">The files a derivation gave, by name: <c>host.json</c> and each <c>&lt;machine&gt;.ir.json</c>.</param>
    internal static IReadOnlyList<Scenario> Explain(IReadOnlyDictionary<string, string> files)
    {
        var host = JsonNode.Parse(files[FeatureDirectory.HostFileName])!.AsObject();
        var irs = files
            .Where(f => f.Key.EndsWith(FeatureDirectory.IrSuffix, StringComparison.Ordinal))
            .ToDictionary(
                f => f.Key[..^FeatureDirectory.IrSuffix.Length],
                f => JsonNode.Parse(f.Value)!.AsObject(),
                StringComparer.Ordinal
            );
        var policies = host["policies"] as JsonArray ?? [];
        var machines = host["machines"] as JsonObject ?? [];

        var scenarios =
            new SortedDictionary<(string File, int Line), (string Name, List<string> Facts)>();
        foreach (var (machine, facts) in host["provenance"] as JsonObject ?? [])
        foreach (var (key, sources) in facts!.AsObject())
        {
            var said = Say(
                machine,
                key,
                machines[machine] as JsonObject,
                irs.GetValueOrDefault(machine),
                policies
            );
            foreach (var source in sources!.AsArray())
            {
                var at = (source!["file"]!.GetValue<string>(), source["line"]!.GetValue<int>());
                if (!scenarios.TryGetValue(at, out var scenario))
                    scenarios[at] = scenario = (source["scenario"]!.GetValue<string>(), []);
                if (!scenario.Facts.Contains(said))
                    scenario.Facts.Add(said);
            }
        }
        return scenarios
            .Select(s => new Scenario(
                s.Key.File,
                s.Key.Line,
                s.Value.Name,
                s.Value.Facts.Order(StringComparer.Ordinal).ToList()
            ))
            .ToList();
    }

    // One provenance key in words. A key this does not know is shown as it is, so a new kind of fact is never hidden.
    private static string Say(
        string machine,
        string key,
        JsonObject? hosted,
        JsonObject? ir,
        JsonArray policies
    )
    {
        var parts = key.Split(' ');
        switch (parts)
        {
            case ["start"]:
                return $"start: {machine} is started by {Rule(hosted?["start"])}";
            case ["command", var trigger]:
                return $"grant: {machine} {trigger} is served to {Rule(hosted?["triggers"]?[trigger])}";
            case ["edge", var from, var trigger, var to]:
                return from == to
                    ? $"edge: {machine} {from} --{trigger}--> {to} (a self-transition: it changes nothing and sets no policy off)"
                    : $"edge: {machine} {from} --{trigger}--> {to}";
            case ["guard", var from, var trigger]:
                return $"guard: {machine} {trigger} from {from} moves when its guard holds and is refused when it fails";
            case ["outcome", var state, var kind]:
                return $"outcome: {machine} {state}'s train ends {kind}{OutcomeTarget(ir, state, kind)}";
            case ["timer", var state]:
                return Timer(machine, state, ir);
            case ["policy", var source, var entered]:
                return Policy(machine, source, entered, policies);
            case ["view", ..]:
                return $"view: '{key[5..]}' is read here, which grants it to the reader's actor";
            default:
                return $"{machine}: {key}";
        }
    }

    // Every role set must be met, by any one role in it, as the host admits a caller.
    internal static string Rule(JsonNode? rule) =>
        rule switch
        {
            null => "nobody",
            JsonObject o when o["server"] is not null => "the server only",
            JsonObject o when o["anyAuthenticated"] is not null => "any signed-in person",
            JsonObject o when o["roles"] is JsonArray sets => string.Join(
                " and ",
                sets.Select(set =>
                    string.Join(" or ", set!.AsArray().Select(r => r!.GetValue<string>()))
                )
            ),
            _ => rule.ToJsonString(),
        };

    private static string OutcomeTarget(JsonObject? ir, string state, string kind)
    {
        var targets = (ir?["outcomes"]?[$"{state}.{kind}"]?["edges"] as JsonArray ?? [])
            .Select(e => e?["to"]?.GetValue<string>())
            .OfType<string>()
            .Distinct()
            .ToList();
        return targets.Count == 0 ? "" : $", to {string.Join(" or ", targets)}";
    }

    private static string Timer(string machine, string state, JsonObject? ir)
    {
        var timer = (ir?["timers"] as JsonArray ?? []).FirstOrDefault(t =>
            t?["state"]?.GetValue<string>() == state
        );
        if (timer is null)
            return $"timer: {machine} {state} has a timer";
        var after = timer["after"]?.GetValue<string>() is { } iso ? Duration(iso) : "a while";
        return $"timer: {machine} leaves {state} after {after} for {timer["target"]?.GetValue<string>()}";
    }

    private static string Duration(string iso)
    {
        try
        {
            var span = XmlConvert.ToTimeSpan(iso);
            return span.TotalDays >= 1 && span.TotalDays % 1 == 0 ? $"{span.TotalDays} d"
                : span.TotalHours >= 1 && span.TotalHours % 1 == 0 ? $"{span.TotalHours} h"
                : span.TotalMinutes >= 1 && span.TotalMinutes % 1 == 0 ? $"{span.TotalMinutes} min"
                : $"{span.TotalSeconds} s";
        }
        catch (FormatException)
        {
            return iso;
        }
    }

    // A policy is recorded under the machine it moves, keyed by the machine and state whose entry sets it off.
    private static string Policy(string target, string source, string entered, JsonArray policies)
    {
        var matches = policies
            .Where(p =>
                p?["when"]?["machine"]?.GetValue<string>() == source
                && p["when"]?["enters"]?.GetValue<string>() == entered
                && p["then"]?["machine"]?.GetValue<string>() == target
            )
            .ToList();
        if (matches.Count == 0)
            return $"policy: when {source} enters {entered}, the server moves {target}";
        return string.Join(
            "; ",
            matches.Select(p =>
            {
                var then = p!["then"]!;
                var trigger = then["trigger"]?.GetValue<string>();
                var which = p["shape"]?.GetValue<string>() switch
                {
                    "toSystem" => then["key"]?.GetValue<string>() ?? target,
                    "broadcast" => $"every {target} in {then["inState"]?.GetValue<string>()}",
                    "linked" => $"the linked {target}",
                    _ => target,
                };
                return $"policy: when {source} enters {entered}, the server fires {trigger} on {which} ({p["shape"]?.GetValue<string>()})";
            })
        );
    }
}
