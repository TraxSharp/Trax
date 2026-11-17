using System.Text;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
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

        foreach (var edge in ir["transitions"].NotNull().AsArray())
            Of(edge.NotNull()["to"].NotNull().GetValue<string>())
                .Add(Text(access["triggers"]?[edge["trigger"].NotNull().GetValue<string>()]));
        if (access["start"] is { } start)
            Of(ir["initialState"].NotNull().GetValue<string>()).Add(Text(start));

        var server = timers
            .Select(t =>
                (
                    From: t.NotNull()["state"].NotNull().GetValue<string>(),
                    To: t["target"].NotNull().GetValue<string>()
                )
            )
            .Concat(
                (ir["outcomes"] as JsonObject ?? []).SelectMany(o =>
                    o.Value.NotNull()["edges"]
                        .NotNull()
                        .AsArray()
                        .Select(e =>
                            (
                                From: o.Value["state"].NotNull().GetValue<string>(),
                                To: e.NotNull()["to"].NotNull().GetValue<string>()
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
            var state = timer.NotNull()["state"].NotNull().GetValue<string>();
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
                    set.NotNull().AsArray() is { Count: 1 } one
                        ? $"Requires({one[0]})"
                        : $"Requires(one of {string.Join(", ", set.AsArray().Select(x => x.NotNull().GetValue<string>()))})"
                )
            ),
            _ => "undeclared",
        };
}
