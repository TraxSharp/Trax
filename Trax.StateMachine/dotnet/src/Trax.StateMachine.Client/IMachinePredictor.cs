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
