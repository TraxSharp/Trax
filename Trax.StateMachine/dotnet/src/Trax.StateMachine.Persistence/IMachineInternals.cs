using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// What a <see cref="Machine{TState,TTrigger}"/> offers this package beyond <see cref="IMachine"/>: its validated
/// initial snapshot, its ownership and invoked trains, and a draft service wired to the invoke outbox.
/// </summary>
internal interface IMachineInternals
{
    /// <summary>
    /// The machine's initial state with <paramref name="context"/> (or the declared initial context when null),
    /// validated like any snapshot, at the machine's current version.
    /// </summary>
    /// <exception cref="ArgumentException">The context is invalid for the initial state, or too large.</exception>
    Snapshot InitialSnapshot(JsonObject? context);

    /// <summary>Whether the machine declares <c>SystemOwned()</c>: only the system holds its instances.</summary>
    bool SystemOwned { get; }

    /// <summary>Who may fire each event, start an instance and read one, as the machine declares it.</summary>
    MachineRules Rules { get; }

    /// <summary>
    /// The triggers with an edge whose guard or reducer is a delegate, which the IR and its hash do not describe.
    /// </summary>
    IReadOnlyCollection<string> DelegateTriggers { get; }

    /// <summary>Every state that invokes a train, as the startup check describes it to the launcher.</summary>
    IReadOnlyList<InvokedTrainDeclaration> InvokedTrains { get; }

    /// <summary>Every state that spawns a child machine. Empty for a machine that spawns none.</summary>
    IReadOnlyList<SpawnDeclaration> Spawns => [];

    /// <summary>Whether the machine invokes a train or spawns a child in any state: its writes go through the outbox.</summary>
    bool WaitsOnWork => InvokedTrains.Count > 0 || Spawns.Count > 0;

    /// <summary>
    /// The states this machine ends in when a parent spawned it, each with the outcome it gives the parent. Empty for
    /// a machine that declares no <c>Ends()</c> or <c>Fails()</c>.
    /// </summary>
    IReadOnlyDictionary<string, InvokeOutcomeKind> Ends =>
        new Dictionary<string, InvokeOutcomeKind>(StringComparer.Ordinal);

    /// <summary>
    /// What a child's context, ended in its <c>Ends()</c> state, gives this machine's spawning state
    /// <paramref name="state"/>: the fields its declared output names, and nothing else.
    /// </summary>
    JsonObject ChildOutput(string state, JsonObject childContext) => childContext;

    /// <summary>
    /// Every outcome edge of an invoking state whose target invokes a train of its own: entering that target would
    /// queue the next run with no user present. Empty for a machine that chains no runs through outcomes.
    /// </summary>
    IReadOnlyList<ChainedOutcome> ChainedOutcomes { get; }

    /// <summary>The invoking state <paramref name="state"/> is, or null when it invokes nothing.</summary>
    EnteringInvoke? Entering(string state);

    /// <summary>Reads stored snapshot JSON, migrating it as a load does. Never throws.</summary>
    RehydrationResult Rehydrate(string json);

    /// <summary>The definition version the machine declares, which a row written by a newer one exceeds.</summary>
    int Version { get; }

    /// <summary>The canonical JSON <paramref name="snapshot"/> is stored and measured as.</summary>
    string Serialize(Snapshot snapshot);

    /// <summary>Applies an invoked run's <paramref name="outcome"/> to <paramref name="snapshot"/>. Never throws.</summary>
    AdvanceResult ApplyOutcome(Snapshot snapshot, InvokeOutcome outcome);

    /// <summary>Fires <paramref name="trigger"/> on <paramref name="snapshot"/> through the engine. Never throws.</summary>
    AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input);

    /// <summary>Whether <paramref name="trigger"/> is an invoked train's outcome trigger, which only its run applies.</summary>
    bool IsOutcomeTrigger(string trigger);

    /// <summary>Whether <paramref name="trigger"/> from <paramref name="state"/> runs the machine's effect.</summary>
    bool IsEffectBound(string state, string trigger);

    /// <summary>
    /// <see cref="IMachine.CreateService"/>, with the cancellation a draft deletion calls first, the runtime a
    /// machine that invokes trains writes through (null for one that invokes none), the outbox that creates a user's
    /// draft under the cap (null for a service that cannot start one), and the cap.
    /// </summary>
    ISnapshotDraftService CreateService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        IInvokedRunCancellation? runCancellation,
        InvokeRuntime? invokes,
        InvokeOutbox? creates,
        DraftCap? cap
    );

    /// <summary>
    /// One sentence per server-written field the machine's declared start reaches; empty when it reaches none. The
    /// host refuses to start while any is listed.
    /// </summary>
    IReadOnlyList<string> StartProblems { get; }

    /// <summary>The effect claim keys of a user's draft, which deleting the draft releases.</summary>
    IEnumerable<string> EffectKeys(string userKey, Guid id);

    /// <summary>
    /// <see cref="EffectKeys"/>, each with the state its effect runs from. A machine that does not say names none.
    /// </summary>
    IEnumerable<DraftClaimHold.EffectKey> EffectKeysWithStates(string userKey, Guid id) =>
        EffectKeys(userKey, id).Select(key => new DraftClaimHold.EffectKey(key, From: null));

    /// <summary>
    /// The states the draft sweep never deletes a user's instance in, however long it has been idle
    /// (<see cref="StateMachineOptions.DraftTtl"/>) or has lived (<see cref="StateMachineOptions.MaxDraftLifetime"/>):
    /// the reserved states (committed, effect targets, outcome targets, gated events' targets), less the states that
    /// invoke a train. An instance in one is the record of what the server did, and how long it stays is the host's to
    /// decide. A machine that does not say keeps none.
    /// </summary>
    IReadOnlySet<string> KeptFromSweep => new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The effect claim keys of <paramref name="owner"/>'s instance <paramref name="id"/>, of either owner kind.</summary>
    IEnumerable<string> EffectKeysOf(DraftOwner owner, Guid id);

    /// <summary>
    /// The effect claim keys of <paramref name="owner"/>'s instance <paramref name="id"/> for the effects that run from
    /// the state named <paramref name="state"/>: the claims that hold that state's timer. Empty when no effect runs
    /// from it.
    /// </summary>
    IEnumerable<string> EffectKeysFrom(DraftOwner owner, Guid id, string state);

    /// <summary>The states with a timer, by name. Empty for a machine with none.</summary>
    IReadOnlySet<string> TimedStates { get; }

    /// <summary>
    /// The triggers only the server fires, by name: a policy's, never served to a client. Empty for a machine with none.
    /// </summary>
    IReadOnlySet<string> ServerTriggers { get; }

    /// <summary>
    /// The edges a server-only trigger fires, in declaration order: what a policy can move an instance through. Empty
    /// for a machine with no server-only trigger.
    /// </summary>
    IReadOnlyList<ServerEdge> ServerEdges { get; }

    /// <summary>Where each timed state's timer goes, by the timed state's name.</summary>
    IReadOnlyDictionary<string, string> TimerTargets { get; }

    /// <summary>
    /// The rules each timer acts under, by its trigger (<c>Review.after</c>): the union of the rules of everything that
    /// enters its state. Empty for a machine with no timer.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> TimerRules { get; }

    /// <summary>
    /// What a row written in the state named <paramref name="state"/> with <paramref name="context"/> does to its timer
    /// (see <see cref="SnapshotMachine{TState,TTrigger}.TimerFor"/>). Never throws.
    /// </summary>
    (SnapshotTimer? Timer, string? Problem) TimerFor(
        string state,
        JsonObject context,
        bool restart
    );

    /// <summary>Whether <paramref name="trigger"/> is a state's timer trigger, which only the server fires.</summary>
    bool IsTimerTrigger(string trigger);

    /// <summary>The timer trigger of the state named <paramref name="state"/>, or null when it has no timer.</summary>
    string? TimerTriggerOf(string state);

    /// <summary>Fires the timer <paramref name="trigger"/> on <paramref name="snapshot"/>. Never throws.</summary>
    AdvanceResult AdvanceTimer(Snapshot snapshot, string trigger);
}
