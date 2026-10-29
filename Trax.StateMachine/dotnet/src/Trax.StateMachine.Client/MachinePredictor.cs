using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>
/// The engine a <see cref="ServerMachineController"/> predicts with: the machine built from the host's IR
/// (<see cref="MachinePredictor.FromIr"/>), or the C# machine compiled into the client. The server applies every transition; a prediction only decides what the client shows until it answers, and is
/// sent beside the event so the server refuses a step the two engines do not agree on.
/// </summary>
public interface IMachinePredictor
{
    /// <summary>The SHA-256 of the machine's IR, which must be the one the host's documents were generated for.</summary>
    string IrHash { get; }

    /// <summary>Applies a trigger to a snapshot, as the server would. Never throws.</summary>
    AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input);

    /// <summary>Reads a snapshot the server sent. Never throws.</summary>
    RehydrationResult Rehydrate(string json);

    /// <summary>The snapshot as canonical wire.</summary>
    string Serialize(Snapshot snapshot);

    /// <summary>Whether <paramref name="trigger"/> fires from <paramref name="snapshot"/> with <paramref name="input"/>.</summary>
    bool CanFire(Snapshot snapshot, string trigger, JsonNode? input);

    /// <summary>The triggers with an edge out of the snapshot's state.</summary>
    IReadOnlyList<string> AvailableTriggers(Snapshot snapshot);

    /// <summary>
    /// The events that take <paramref name="snapshot"/> to <paramref name="target"/> with the page's fields applied
    /// (<see cref="IrMachine.Plan"/>). An edge this predictor cannot tell the outcome of answers
    /// <see cref="PlanResult.Undetermined"/> rather than a guess. Never throws.
    /// </summary>
    PlanResult Plan(
        Snapshot snapshot,
        string target,
        PlannedEvent? fields,
        IReadOnlyList<string> navigation,
        int? maxDepth = null
    );
}

/// <summary>Builds an <see cref="IMachinePredictor"/>, from the host's IR or from a compiled machine.</summary>
public static class MachinePredictor
{
    /// <summary>A predictor over <paramref name="machine"/>'s engine, whose IR hash is that of its exported IR.</summary>
    public static IMachinePredictor For<TState, TTrigger>(BuiltMachine<TState, TTrigger> machine)
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(machine);
        return new EnginePredictor<TState, TTrigger>(machine);
    }

    /// <summary>
    /// A predictor built from the machine's IR as the host's client manifest lists it, the way every language's client
    /// builds its machine: no reference to the host's assembly. A snapshot the server sends is read trusting the custom
    /// state requirements it has no handler for, since the server checked them; an event into such a state is not
    /// predicted.
    /// </summary>
    /// <exception cref="ArgumentException">The manifest's IR is not one the engine can read.</exception>
    public static IMachinePredictor FromIr(ClientManifestMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return new IrPredictor(
            new IrMachine((JsonObject)machine.Ir.DeepClone()),
            Hash(machine.IrText())
        );
    }

    private static string Hash(string ir) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ir)));

    // A machine built from the IR binds no handler to a custom rule, so without one its guard fails and its reduction
    // keeps the context, which is not what the server does: an edge that rests on one is not predicted. The TypeScript
    // and Rust clients read the same edges the same way.
    private sealed class IrPredictor(IrMachine machine, string irHash)
        : IMachinePredictor,
            IPartialPredictor
    {
        public string IrHash { get; } = irHash;

        public bool Predicts(string state, string trigger) => machine.Determines(state, trigger);

        public AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input) =>
            machine.Advance(snapshot, trigger, input);

        public RehydrationResult Rehydrate(string json) =>
            machine.Rehydrate(json, fromServer: true);

        public string Serialize(Snapshot snapshot) => machine.Serialize(snapshot);

        public bool CanFire(Snapshot snapshot, string trigger, JsonNode? input) =>
            machine.CanFire(snapshot, trigger, input);

        public IReadOnlyList<string> AvailableTriggers(Snapshot snapshot) =>
            machine.AvailableTriggers(snapshot);

        public PlanResult Plan(
            Snapshot snapshot,
            string target,
            PlannedEvent? fields,
            IReadOnlyList<string> navigation,
            int? maxDepth = null
        ) => machine.Plan(snapshot, target, fields, navigation, maxDepth);
    }

    private sealed class EnginePredictor<TState, TTrigger>(BuiltMachine<TState, TTrigger> machine)
        : IMachinePredictor
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        private readonly SnapshotMachine<TState, TTrigger> _engine = machine.Engine;

        public string IrHash { get; } = Hash(IrExporter.Export(machine));

        public AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input) =>
            _engine.Advance(snapshot, trigger, input);

        public RehydrationResult Rehydrate(string json) => _engine.Rehydrate(json);

        public string Serialize(Snapshot snapshot) => _engine.Serialize(snapshot);

        public bool CanFire(Snapshot snapshot, string trigger, JsonNode? input) =>
            _engine.CanFire(snapshot, trigger, input);

        public IReadOnlyList<string> AvailableTriggers(Snapshot snapshot) =>
            _engine.AvailableTriggers(snapshot);

        public PlanResult Plan(
            Snapshot snapshot,
            string target,
            PlannedEvent? fields,
            IReadOnlyList<string> navigation,
            int? maxDepth = null
        ) => machine.Plan(snapshot, target, fields, navigation, maxDepth);
    }
}

// A predictor that cannot tell what every event leads to: one built from the IR, which holds no handler for a custom
// rule. The controller sends such an event with no prediction.
internal interface IPartialPredictor
{
    bool Predicts(string state, string trigger);
}

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
                    g => (IReadOnlyList<string>)g.Select(t => t.Trigger).Distinct().ToList(),
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
