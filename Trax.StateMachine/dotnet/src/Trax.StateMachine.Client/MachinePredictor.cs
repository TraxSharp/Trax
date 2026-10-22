using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>
/// The engine a <see cref="ServerMachineController"/> predicts with: the C# machine the host serves, compiled into the
/// client. The server applies every transition; a prediction only decides what the client shows until it answers, and is
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

/// <summary>Builds the <see cref="IMachinePredictor"/> for a compiled machine.</summary>
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

    private sealed class EnginePredictor<TState, TTrigger>(BuiltMachine<TState, TTrigger> machine)
        : IMachinePredictor
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        private readonly SnapshotMachine<TState, TTrigger> _engine = machine.Engine;

        public string IrHash { get; } =
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(IrExporter.Export(machine)))
            );

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
