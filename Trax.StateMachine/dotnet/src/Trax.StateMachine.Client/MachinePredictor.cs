using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

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
