using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

// What the controller and the server read from a machine's IR: its id and version, the states with a timer, the
// triggers bound to an effect, the triggers a client may send from each state, and the triggers it may send at all.
internal sealed class MachineShape
{
    // The operations every machine is served with, which no event is sent as: an event's field named like one would
    // resolve to that operation's document.
    public static readonly IReadOnlySet<string> Operations = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "start",
        "get",
        "discard",
        "changed",
    };

    public required string Id { get; init; }
    public required int Version { get; init; }
    public required int StateCount { get; init; }
    public required IReadOnlySet<string> TimedStates { get; init; }
    public required IReadOnlySet<string> EffectTriggers { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> TriggersFrom { get; init; }
    public required IReadOnlySet<string> Events { get; init; }
    public required IReadOnlySet<string> OutcomeTriggers { get; init; }
    public required IReadOnlySet<string> TimerTriggers { get; init; }

    // Why the engine's advance refuses a trigger the machine does not declare as an event, as it refuses it: an
    // invoked train's outcome, a state's timer, or a name it does not know. Null for a declared event.
    public (RefusalCode Code, string Message)? Undeclared(string trigger) =>
        OutcomeTriggers.Contains(trigger)
            ? (
                RefusalCode.OutcomeBound,
                "This step is completed by the outcome of its work, not by an action."
            )
        : TimerTriggers.Contains(trigger)
            ? (
                RefusalCode.TimerBound,
                "This step moves on by itself when its time is up, not by an action."
            )
        : Events.Contains(trigger) ? null
        : (RefusalCode.NoTransition, $"Unknown trigger '{trigger}'.");

    public static MachineShape Of(ClientManifestMachine machine)
    {
        var ir = machine.Ir;
        var transitions = (ir["transitions"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(t =>
                (
                    From: t["from"]?.GetValue<string>() ?? "",
                    Trigger: t["trigger"]?.GetValue<string>() ?? "",
                    Effect: t["effect"] is not null
                )
            )
            .ToList();
        return new MachineShape
        {
            Id = machine.Id,
            Version = ir["version"]?.GetValue<int>() ?? 1,
            StateCount = (ir["states"] as JsonArray)?.Count ?? 0,
            TimedStates = (ir["timers"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(t => t["state"]?.GetValue<string>() ?? "")
                .ToHashSet(StringComparer.Ordinal),
            EffectTriggers = transitions
                .Where(t => t.Effect)
                .Select(t => t.Trigger)
                .ToHashSet(StringComparer.Ordinal),
            TriggersFrom = transitions
                .GroupBy(t => t.From, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g =>
                        (IReadOnlyList<string>)
                            g.Select(t => t.Trigger).Distinct(StringComparer.Ordinal).ToList(),
                    StringComparer.Ordinal
                ),
            Events = (ir["triggers"] as JsonArray ?? [])
                .OfType<JsonValue>()
                .Select(t => t.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal),
            OutcomeTriggers = (ir["outcomes"] as JsonObject ?? [])
                .Select(o => o.Key)
                .ToHashSet(StringComparer.Ordinal),
            TimerTriggers = (ir["timers"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(t => t["trigger"]?.GetValue<string>() ?? "")
                .ToHashSet(StringComparer.Ordinal),
        };
    }
}
