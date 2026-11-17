using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Utils;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The base class a machine subclasses. Override <see cref="Configure"/> to declare the machine fluently
/// (states, transitions, guards, reducers, committed states, and the one irreversible effect), and a host
/// discovers it and wires everything, no per-machine registration, no effect wiring in the composition root.
///
/// <code>
/// public sealed class Checkout : Machine&lt;CheckoutState, CheckoutTrigger&gt;
/// {
///     protected override void Configure(IMachineBuilder&lt;CheckoutState, CheckoutTrigger&gt; m)
///     {
///         m.Id("checkout").Version(1).StartsAt(CheckoutState.Cart, FreshCart);
///         m.In(CheckoutState.Review).On(CheckoutTrigger.Pay).When(Payable).RunsOnce&lt;ICharge&gt;().Reduce(ApplyReceipt).To(CheckoutState.Paid);
///         m.In(CheckoutState.Paid).Committed();
///     }
/// }
/// </code>
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine">SDK reference</seealso>
public abstract class Machine<TState, TTrigger> : IMachine, IMachineInternals
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    // A machine is a singleton that concurrent requests read, so both lazies publish exactly one value and
    // every reader waits for it: a reader during the first Configure must not see a half-built machine or a
    // null schema hash, because a null hash switches the schema-mismatch check off.
    private readonly Lazy<BuiltMachine<TState, TTrigger>> _built;
    private readonly Lazy<string?> _schemaHash;

    /// <summary>
    /// Creates the machine. <see cref="Configure"/> is not called here: it runs once, lazily and thread-safely,
    /// the first time any member needs the built machine.
    /// </summary>
    protected Machine()
    {
        _built = new(BuildOnce, LazyThreadSafetyMode.ExecutionAndPublication);
        _schemaHash = new(ComputeSchemaHash, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private BuiltMachine<TState, TTrigger> Built => _built.Value;

    private BuiltMachine<TState, TTrigger> BuildOnce()
    {
        var builder = new MachineBuilder<TState, TTrigger>();
        Configure(builder);
        return builder.Build();
    }

    /// <summary>Declare the machine. Everything about it lives here, on the transitions it belongs to.</summary>
    protected abstract void Configure(IMachineBuilder<TState, TTrigger> machine);

    /// <inheritdoc/>
    public string Name => Built.Definition.Id;

    /// <inheritdoc/>
    public bool HasEffect => Built.Effects.Count > 0;

    /// <summary>
    /// Every trigger the machine declares: each member of <typeparamref name="TTrigger"/>, as
    /// <see cref="TriggerKind.EffectBound"/> when an edge on it binds the machine's effect and
    /// <see cref="TriggerKind.Client"/> otherwise, with the record its <c>WithInput</c> declares; then each
    /// invoking state's outcome triggers (<c>Building.done</c>, <c>.failed</c>, <c>.cancelled</c>) as
    /// <see cref="TriggerKind.Outcome"/>, the success outcome's input being the train's output type.
    /// </summary>
    public IReadOnlyList<MachineTrigger> Triggers => Built.Triggers;

    /// <summary>
    /// Every member of <typeparamref name="TState"/>, in declaration order, with the record its
    /// <c>Context&lt;TContext&gt;()</c> declares, or null for a state declared with <c>Context()</c> or none.
    /// </summary>
    public IReadOnlyList<MachineState> States => Built.States;

    /// <summary>The record the machine's <c>Start&lt;TStartInput&gt;</c> declares, or null when it declares no start.</summary>
    public Type? StartInputType => Built.Start?.InputType;

    /// <inheritdoc/>
    public string ExportIr() => IrExporter.Export(Built);

    /// <summary>
    /// The lowercase hex SHA-256 of <see cref="ExportIr"/>, computed once. Null for a machine whose IR export is
    /// refused by design: a raw-delegate machine, or one that declares rules but still has a delegate guard,
    /// reducer or validator. Null switches the client/server schema-mismatch check off for this machine. Any other
    /// failure computing it is rethrown to every reader rather than answered with null.
    /// </summary>
    public string? SchemaHash => _schemaHash.Value;

    // A machine with a delegate in it (all raw delegates, or rules mixed with a delegate guard, reducer or
    // validator) has no exportable IR, so it has no frontend twin, no schema hash, and no handshake: null, which
    // the guard treats as "no check" and a write records as no hash. Serving such a machine through typed
    // operations is refused separately, where it is exposed. Any other failure propagates, and the lazy rethrows
    // it to every reader rather than answering null, which would switch the check off for a machine that should
    // have one.
    private string? ComputeSchemaHash() =>
        IrExporter.RefusesByDesign(Built)
            ? null
            : Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(ExportIr())
                )
            );

    /// <summary>Override to ship a committed differential corpus (e.g. an embedded resource); null = none.</summary>
    public virtual string? Corpus => null;

    /// <inheritdoc/>
    public IReadOnlyList<string> SelfCheck() =>
        Corpus is { } corpus ? CorpusReplay.Replay(Built.Engine, corpus) : Array.Empty<string>();

    /// <summary>
    /// Builds a draft service over <paramref name="store"/>. This is the entry point for unit-testing a machine
    /// against an in-memory <see cref="ISnapshotStore"/>; a host normally gets the service from
    /// <see cref="ISnapshotMachineRegistry.Service"/>. With <paramref name="claims"/> set, resetting a draft also
    /// releases the machine's effect claims for it (key <c>{prefix}:{userKey}:{draftId}</c>) so the effect can
    /// run again.
    /// </summary>
    /// <param name="store">Where drafts are read and written.</param>
    /// <param name="claims">The effect-claim ledger to clear on reset, or null to leave claims alone.</param>
    /// <param name="draftTtl">
    /// Not read. A read never deletes a draft: expiry belongs to the draft sweep a host's <c>AddStateMachines</c>
    /// runs, under <see cref="StateMachineOptions.DraftTtl"/>.
    /// </param>
    public ISnapshotDraftService CreateService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        TimeSpan? draftTtl = null
    ) =>
        BuildService(store, claims, runCancellation: null, invokes: null, creates: null, cap: null);

    ISnapshotDraftService IMachineInternals.CreateService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        IInvokedRunCancellation? runCancellation,
        InvokeRuntime? invokes,
        InvokeOutbox? creates,
        DraftCap? cap
    ) => BuildService(store, claims, runCancellation, invokes, creates, cap);

    private SnapshotDraftService<TState, TTrigger> BuildService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        IInvokedRunCancellation? runCancellation,
        InvokeRuntime? invokes,
        InvokeOutbox? creates,
        DraftCap? cap
    ) =>
        new(Built.Engine, store, Built.CommittedStates, claims, EffectKeysOnReset, Built.Effects)
        {
            RunCancellation = runCancellation,
            ReservedStates = Built.ReservedStates,
            InvokingStates = Built.Invokes.Keys.ToHashSet(),
            TimedStates = Built.Timers.Keys.ToHashSet(),
            Invokes = invokes,
            InvokedRunLimit = Built.InvokedRunLimit,
            Rules = Built.Rules,
            SaveNotAllowed = HasServerBehaviour,
            StartDeclaration = Built.Start,
            SystemOwned = Built.SystemOwned,
            Creates = creates,
            Cap = cap,
        };

    IReadOnlyList<string> IMachineInternals.StartProblems => Built.StartProblems();

    IEnumerable<string> IMachineInternals.EffectKeys(string userKey, Guid id) =>
        EffectKeysOnReset(userKey, id);

    bool IMachineInternals.SystemOwned => Built.SystemOwned;

    MachineRules IMachineInternals.Rules => Built.Rules;

    IReadOnlyCollection<string> IMachineInternals.DelegateTriggers =>
        Built.DelegateTriggers.Select(t => t.ToString()).ToList();

    // The generic save serves only machines with no server-side behaviour: an event or a start gated by a role or a
    // policy, an invoked train, an effect or a timer each move the machine through a declared event, the start's rule
    // or the server, which the save would step around. So does anything an edge or the start decides or computes: a
    // guard, a reducer that changes the context, a custom guard or reducer bound by name, a start that reduces its
    // input. A save would store a state or a context none of them let through.
    // A child machine ends its parent's wait, so a save, which could put it in its end state, is refused too.
    private bool HasServerBehaviour =>
        Built.Rules.AnyGated
        || Built.HasEdgeLogic
        || Built.BindsCustomHandlers
        || Built.StartReducesInput
        || Built.Invokes.Count > 0
        || Built.Effects.Count > 0
        || Built.Timers.Count > 0
        || Built.Definition.Ends.Count > 0;

    IReadOnlyList<InvokedTrainDeclaration> IMachineInternals.InvokedTrains =>
        Built
            .Invokes.Values.Where(i => i.Spawn is null)
            .Select(i => new InvokedTrainDeclaration(
                Built.Definition.Id,
                i.State.ToString(),
                i.TrainType,
                i.InputType,
                i.OutputType,
                Built.SystemOwned
            )
            {
                EnteringEvents = EnteringEvents(i.State),
            })
            .ToList();

    // Every event that enters an invoking state, with its rule, and the start when the machine starts in it: each is
    // a way a caller makes the state's train run. A self-loop does not enter the state.
    private IReadOnlyList<EnteringEvent> EnteringEvents(TState state)
    {
        var entering = Built
            .Definition.Transitions.Where(t =>
                EqualityComparer<TState>.Default.Equals(t.To, state)
                && !EqualityComparer<TState>.Default.Equals(t.From, state)
            )
            .Select(t => t.Trigger.ToString())
            .Distinct(StringComparer.Ordinal)
            .Select(trigger => new EnteringEvent(
                trigger,
                Built.Rules.Events.GetValueOrDefault(trigger)
            ))
            .ToList();
        if (EqualityComparer<TState>.Default.Equals(Built.Definition.InitialState, state))
            entering.Insert(0, new EnteringEvent(EnteringEvent.Start, Built.Rules.Start));
        return entering;
    }

    IReadOnlyList<ChainedOutcome> IMachineInternals.ChainedOutcomes =>
        Built
            .Invokes.Values.SelectMany(i =>
                i.Done.Select(e => (Outcome: "OnDone", e.To))
                    .Append((Outcome: "OnFailed", i.Failed.To))
                    .Append((Outcome: "OnCancelled", i.Cancelled.To))
                    .Where(e => Built.Invokes.ContainsKey(e.To))
                    .Select(e => new ChainedOutcome(i.State.ToString(), e.Outcome, e.To.ToString()))
            )
            .Distinct()
            .ToList();

    EnteringInvoke? IMachineInternals.Entering(string state) =>
        Built.Invokes.Values.FirstOrDefault(i =>
            string.Equals(i.State.ToString(), state, StringComparison.Ordinal)
        )
            is { } invoke
            ? new EnteringInvoke(invoke.TrainType, invoke.CreateInput, Built.InvokedRunLimit)
            {
                Spawn = invoke.Spawn,
            }
            : null;

    IReadOnlyList<SpawnDeclaration> IMachineInternals.Spawns =>
        Built
            .Invokes.Values.Where(i => i.Spawn is not null)
            .Select(i => new SpawnDeclaration(
                Built.Definition.Id,
                i.State.ToString(),
                i.Spawn!.ChildMachine,
                Built.SystemOwned
            ))
            .ToList();

    IReadOnlyDictionary<string, InvokeOutcomeKind> IMachineInternals.Ends =>
        Built.Definition.Ends.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value,
            StringComparer.Ordinal
        );

    JsonObject IMachineInternals.ChildOutput(string state, JsonObject childContext)
    {
        var fields =
            Built
                .Invokes.Values.FirstOrDefault(i =>
                    string.Equals(i.State.ToString(), state, StringComparison.Ordinal)
                )
                ?.OutputSchema.Fields
            ?? [];
        var output = new JsonObject();
        foreach (var field in fields)
            if (childContext.TryGetPropertyValue(field.Name, out var value))
                output[field.Name] = value?.DeepClone();
        return output;
    }

    RehydrationResult IMachineInternals.Rehydrate(string json) => Built.Engine.Rehydrate(json);

    int IMachineInternals.Version => Built.Definition.Version;

    string IMachineInternals.Serialize(Snapshot snapshot) => Built.Engine.Serialize(snapshot);

    AdvanceResult IMachineInternals.ApplyOutcome(Snapshot snapshot, InvokeOutcome outcome)
    {
        // The startup check refuses a sensitive output and the launch refuses to queue its train; this refuses the
        // output again, fail-closed, so it never reaches the context, which is stored as plain JSON. The delivery
        // applies the state's OnFailed instead.
        if (
            outcome is InvokeOutcome.Done
            && Built.Invokes.Values.FirstOrDefault(i =>
                string.Equals(i.State.ToString(), snapshot.State, StringComparison.Ordinal)
            )
                is { } invoke
            && TraxRedaction.ReachesSensitiveMember(invoke.OutputType)
        )
            return new AdvanceResult.Rejected(RefusalCode.InternalError)
            {
                Exception = new InvalidOperationException(
                    InvokeRefusals.SensitiveOutput(
                        InvokeRefusals.At(Built.Definition.Id, snapshot.State, invoke.TrainType),
                        invoke.OutputType
                    )
                ),
            };

        return Built.Engine.ApplyOutcome(snapshot, outcome);
    }

    AdvanceResult IMachineInternals.Advance(Snapshot snapshot, string trigger, JsonNode? input) =>
        Built.Engine.Advance(snapshot, trigger, input);

    bool IMachineInternals.IsOutcomeTrigger(string trigger) =>
        Built.Engine.IsOutcomeTrigger(trigger);

    bool IMachineInternals.IsEffectBound(string state, string trigger) =>
        Built.Effects.Any(e =>
            string.Equals(e.From.ToString(), state, StringComparison.Ordinal)
            && string.Equals(e.Trigger.ToString(), trigger, StringComparison.Ordinal)
        );

    private IEnumerable<string> EffectKeysOnReset(string userKey, Guid id) =>
        Built.Effects.Select(e => EffectClaimKey.ForUser(e.KeyPrefix, userKey, id));

    IEnumerable<string> IMachineInternals.EffectKeysOf(DraftOwner owner, Guid id) =>
        owner.UserKey is { } userKey
            ? EffectKeysOnReset(userKey, id)
            : Built.Effects.Select(e => EffectClaimKey.ForSystem(e.KeyPrefix, id));

    IEnumerable<string> IMachineInternals.EffectKeysFrom(DraftOwner owner, Guid id, string state) =>
        Built
            .Effects.Where(e => string.Equals(e.From.ToString(), state, StringComparison.Ordinal))
            .Select(e =>
                owner.UserKey is { } userKey
                    ? EffectClaimKey.ForUser(e.KeyPrefix, userKey, id)
                    : EffectClaimKey.ForSystem(e.KeyPrefix, id)
            );

    IEnumerable<DraftClaimHold.EffectKey> IMachineInternals.EffectKeysWithStates(
        string userKey,
        Guid id
    ) =>
        Built.Effects.Select(e => new DraftClaimHold.EffectKey(
            EffectClaimKey.ForUser(e.KeyPrefix, userKey, id),
            e.From.ToString()
        ));

    IReadOnlySet<string> IMachineInternals.KeptFromSweep =>
        Built
            .ReservedStates.Except(Built.Invokes.Keys)
            .Select(s => s.ToString())
            .ToHashSet(StringComparer.Ordinal);

    IReadOnlyDictionary<string, string> IMachineInternals.TimerTargets =>
        Built.Timers.Values.ToDictionary(
            t => t.State.ToString(),
            t => t.Target.ToString(),
            StringComparer.Ordinal
        );

    IReadOnlySet<string> IMachineInternals.ServerTriggers =>
        Built.ServerOnly.Select(t => t.ToString()).ToHashSet(StringComparer.Ordinal);

    IReadOnlyList<ServerEdge> IMachineInternals.ServerEdges =>
        Built
            .Definition.Transitions.Where(t => Built.ServerOnly.Contains(t.Trigger))
            .Select(t => new ServerEdge(
                t.From.ToString()!,
                t.Trigger.ToString()!,
                t.To.ToString()!
            ))
            .Distinct()
            .ToList();

    IReadOnlySet<string> IMachineInternals.TimedStates =>
        Built.Timers.Keys.Select(s => s.ToString()).ToHashSet(StringComparer.Ordinal);

    IReadOnlyDictionary<string, IReadOnlyList<string>> IMachineInternals.TimerRules =>
        Built.TimerRules();

    (SnapshotTimer? Timer, string? Problem) IMachineInternals.TimerFor(
        string state,
        JsonObject context,
        bool restart
    ) => Built.Engine.TimerFor(state, context, restart);

    bool IMachineInternals.IsTimerTrigger(string trigger) => Built.Engine.IsTimerTrigger(trigger);

    string? IMachineInternals.TimerTriggerOf(string state) =>
        Built.Engine.TimerOf(state)?.TriggerName;

    AdvanceResult IMachineInternals.AdvanceTimer(Snapshot snapshot, string trigger) =>
        Built.Engine.AdvanceTimer(snapshot, trigger);

    Snapshot IMachineInternals.InitialSnapshot(JsonObject? context)
    {
        var engine = Built.Engine;
        var definition = engine.Definition;
        var initial = definition.CreateInitialSnapshot();
        if (context is not null)
            initial = initial with { Context = (JsonObject)context.DeepClone() };

        // Validated exactly as a stored snapshot is read back, so nothing is stored that a later read refuses.
        var json = engine.Serialize(initial);
        if (Encoding.UTF8.GetByteCount(json) > SnapshotLimits.MaxSnapshotBytes)
            throw new ArgumentException(
                $"The initial snapshot of '{definition.Id}' exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit.",
                nameof(context)
            );
        return engine.Rehydrate(json) switch
        {
            // Created in its initial state, the instance enters it: a timed initial state's timer starts now, and a
            // duration its context does not hold within bounds refuses the start.
            RehydrationResult.Ok ok => engine.TimerFor(
                ok.Snapshot.State,
                ok.Snapshot.Context,
                restart: true
            )
                is ({ } timer, _)
                ? ok.Snapshot with
                {
                    Timer = timer,
                }
                : throw new ArgumentException(
                    $"The initial snapshot of '{definition.Id}' is not valid ({RefusalCode.InvalidInput.ToWire()}): "
                        + engine
                            .TimerFor(ok.Snapshot.State, ok.Snapshot.Context, restart: true)
                            .Problem,
                    nameof(context)
                ),
            RehydrationResult.Error error => throw new ArgumentException(
                $"The initial snapshot of '{definition.Id}' is not valid ({error.Code.ToWire()}): {error.Message}",
                nameof(context),
                error.Exception
            ),
            _ => throw new ArgumentException("Unknown rehydration result.", nameof(context)),
        };
    }

    /// <summary>
    /// Builds the exactly-once runner for the machine's first bound effect, resolving the effect type from
    /// <paramref name="services"/>; returns null when the machine binds no effect. Infrastructure used by
    /// <see cref="ISnapshotMachineRegistry"/>; not intended to be called directly.
    /// </summary>
    /// <param name="service">
    /// The draft service from this machine's <see cref="CreateService"/>. Any other implementation throws
    /// <see cref="ArgumentException"/>.
    /// </param>
    /// <param name="idempotent">The exactly-once primitive the runner claims the effect key through.</param>
    /// <param name="services">The container the effect is resolved from; the effect must be registered.</param>
    /// <exception cref="ArgumentException"><paramref name="service"/> was not created by this machine's <see cref="CreateService"/>.</exception>
    /// <exception cref="InvalidOperationException">The effect type is not registered in <paramref name="services"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ISnapshotEffectRunner? CreateEffectRunner(
        ISnapshotDraftService service,
        IdempotentEffect idempotent,
        IServiceProvider services
    )
    {
        if (Built.Effects.Count == 0)
            return null;

        // The runner commits the effect's result through the draft service's effect-only advance, which only
        // the service this machine built has. Say so rather than failing on a cast.
        if (service is not SnapshotDraftService<TState, TTrigger> drafts)
            throw new ArgumentException(
                $"CreateEffectRunner needs the draft service built by this machine's CreateService, but was given "
                    + $"{service?.GetType().Name ?? "null"}. Get the runner from ISnapshotMachineRegistry.EffectRunner, "
                    + "or pass the service CreateService returned.",
                nameof(service)
            );

        var binding = Built.Effects[0];
        var effect = (ISnapshotEffect)services.GetRequiredService(binding.EffectType);
        return new SnapshotEffectRunner<TState, TTrigger>(
            drafts,
            effect,
            idempotent,
            binding.From,
            binding.Trigger,
            binding.To,
            (userKey, id) => EffectClaimKey.ForUser(binding.KeyPrefix, userKey, id),
            receiptKey: "receipt"
        );
    }
}
