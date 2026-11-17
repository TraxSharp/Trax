using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

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
