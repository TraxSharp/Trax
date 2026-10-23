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
            Hash(machine.IrText()),
            UnpredictableEdges(machine.Ir)
        );
    }

    // The (state, trigger) pairs with an edge whose guard names a custom rule, whose reduction is custom, or whose
    // destination's requirement names a custom rule. A machine built from the IR binds no handler to any, so without one
    // its guard fails and its reduction keeps the context, which is not what the server does. The TypeScript and Rust
    // clients read the same edges the same way.
    internal static IReadOnlySet<(string State, string Trigger)> UnpredictableEdges(JsonObject ir)
    {
        var invariants = ir["invariants"] as JsonObject;
        return (ir["transitions"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(t =>
                NamesCustom(t["guard"])
                || (
                    t["reduce"]?["reduce"] is JsonValue r
                    && r.TryGetValue<string>(out var kind)
                    && kind == "custom"
                )
                || (
                    t["to"] is JsonValue to
                    && to.TryGetValue<string>(out var target)
                    && NamesCustom(invariants?[target])
                )
            )
            .Select(t =>
                (t["from"]?.GetValue<string>() ?? "", t["trigger"]?.GetValue<string>() ?? "")
            )
            .ToHashSet();
    }

    private static bool NamesCustom(JsonNode? rule) =>
        rule is JsonObject r
        && (r["rule"] as JsonValue)?.GetValue<string>() switch
        {
            "custom" => true,
            "all" or "any" => (r["rules"] as JsonArray ?? []).Any(NamesCustom),
            _ => false,
        };

    private static string Hash(string ir) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ir)));

    private sealed class IrPredictor(
        IrMachine machine,
        string irHash,
        IReadOnlySet<(string, string)> unpredictable
    ) : IMachinePredictor, IPartialPredictor
    {
        public string IrHash { get; } = irHash;

        public bool Predicts(string state, string trigger) =>
            !unpredictable.Contains((state, trigger));

        public AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input) =>
            machine.Advance(snapshot, trigger, input);

        public RehydrationResult Rehydrate(string json) =>
            machine.Rehydrate(json, fromServer: true);

        public string Serialize(Snapshot snapshot) => machine.Serialize(snapshot);

        public bool CanFire(Snapshot snapshot, string trigger, JsonNode? input) =>
            machine.CanFire(snapshot, trigger, input);

        public IReadOnlyList<string> AvailableTriggers(Snapshot snapshot) =>
            machine.AvailableTriggers(snapshot);
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
    }
}

// A predictor that cannot tell what every event leads to: one built from the IR, which holds no handler for a custom
// rule. The controller sends such an event with no prediction.
internal interface IPartialPredictor
{
    bool Predicts(string state, string trigger);
}

// What the controller and the server read from a machine's IR: its id and version, the states with a timer, the
// triggers bound to an effect, and the triggers a client may send from each state.
internal sealed class MachineShape
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required IReadOnlySet<string> TimedStates { get; init; }
    public required IReadOnlySet<string> EffectTriggers { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> TriggersFrom { get; init; }

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
        };
    }
}
