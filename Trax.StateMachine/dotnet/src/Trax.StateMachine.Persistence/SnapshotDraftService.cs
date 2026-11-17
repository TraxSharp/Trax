using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// Infrastructure behind <see cref="ISnapshotDraftService"/>, built by <see cref="IMachine.CreateService"/>;
/// not intended to be constructed directly. The FE-drives / BE-validates operations over a persisted, user-scoped snapshot, built on the total
/// <see cref="SnapshotMachine{TState,TTrigger}"/> engine and an <see cref="ISnapshotStore"/>. Every
/// method is total for expected outcomes — including concurrency conflicts, which come back as a typed
/// <c>Conflict</c> rather than a thrown <c>DbUpdateException</c>.
/// </summary>
internal sealed class SnapshotDraftService<TState, TTrigger>(
    SnapshotMachine<TState, TTrigger> machine,
    ISnapshotStore store,
    IReadOnlyCollection<TState>? committedStates = null,
    IEffectClaimStore? effectClaims = null,
    Func<string, Guid, IEnumerable<string>>? effectKeysOnReset = null,
    IReadOnlyList<EffectBinding<TState, TTrigger>>? effects = null
) : ISnapshotDraftService, IEffectRunnerSlot, ISnapshotWatchAccess, IEndNotifierSlot
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    // States a SOFT autosave must not move a draft OUT of (except a reset to the initial state) — e.g. a
    // Paid order. This is what stops a stale/racing autosave from resurrecting a completed draft and
    // letting an irreversible action happen a second time. Empty => the fast last-writer-wins path.
    private readonly HashSet<string> _committedStates = BuildStateSet(committedStates);
    private readonly string _initialState = machine.Definition.InitialState.ToString()!;

    // A draft belongs to one machine: every read and delete names it, so two machines' drafts under one id
    // stay apart.
    private readonly string _machineId = machine.Definition.Id;

    /// <summary>The id of the machine whose drafts this service reads and writes.</summary>
    internal string MachineId => _machineId;

    // The (from, trigger) edges that run the machine's irreversible effect. Only the effect runner fires them,
    // after the effect ran, so the receipt the reducer records is one the effect produced.
    private readonly HashSet<(string From, string Trigger)> _effectEdges =
        effects?.Select(e => (e.From.ToString()!, e.Trigger.ToString()!)).ToHashSet() ?? [];

    // The states the soft path may not create or move a draft into: every committed state and every state an
    // effect-bound transition lands in, which a draft reaches through the effect runner only; and, for a machine
    // that invokes trains, every invoking state and every state an invoked train's outcome reaches, which a draft
    // reaches through an advance or an outcome only; and every target of a gated event. Discard refuses them all.
    // Built on first use, after the init-only members are set.
    private HashSet<string>? _reservedCache;

    private HashSet<string> ReservedNames =>
        _reservedCache ??= ReservedStates is { } reserved
            ? reserved.Select(s => s.ToString()!).ToHashSet(StringComparer.Ordinal)
            : BuildStateSet(committedStates)
                .Concat(effects?.Select(e => e.To.ToString()!) ?? [])
                .ToHashSet(StringComparer.Ordinal);

    // The states the soft path may not take a draft out of or rewrite: committed states and effect targets (a
    // reset the machine declares excepted), invoking states, which only a declared transition or an outcome
    // leaves, so autosave never strands a run or stands in for one, and timed states, which only an event or the timer leaves,
    // so autosave never skips a deadline or rewrites the context it fires on.
    private HashSet<string>? _lockedCache;

    private HashSet<string> LockedNames =>
        _lockedCache ??= BuildStateSet(committedStates)
            .Concat(effects?.Select(e => e.To.ToString()!) ?? [])
            .Concat(InvokingNames)
            .Concat(TimedStates?.Select(s => s.ToString()!) ?? [])
            .ToHashSet(StringComparer.Ordinal);

    private HashSet<string>? _invokingCache;

    private HashSet<string> InvokingNames =>
        _invokingCache ??=
            InvokingStates?.Select(s => s.ToString()!).ToHashSet(StringComparer.Ordinal) ?? [];

    /// <summary>
    /// Every state the soft path may not move a draft into (see <c>BuiltMachine.ReservedStates</c>). Null keeps the
    /// committed states and effect targets only, for a service built without the machine's invoke declarations.
    /// </summary>
    internal IReadOnlySet<TState>? ReservedStates { get; init; }

    /// <summary>The states that invoke a train. Entering one queues a run; leaving one cancels it.</summary>
    internal IReadOnlySet<TState>? InvokingStates { get; init; }

    /// <summary>The states with a timer. Entering one arms it; leaving one cancels it; the soft path locks them.</summary>
    internal IReadOnlySet<TState>? TimedStates { get; init; }

    /// <summary>
    /// The store and outbox a machine that invokes trains writes through. Null for a service built without them,
    /// which refuses to enter or leave an invoking state rather than write one without its run.
    /// </summary>
    internal InvokeRuntime? Invokes { get; init; }

    /// <summary>The most live invoked runs, across every machine, one user may hold to enter this machine's invoking states.</summary>
    internal int InvokedRunLimit { get; init; } =
        BuiltMachine<TState, TTrigger>.DefaultInvokedRunLimit;

    /// <summary>Who may fire each event, as the machine declares it.</summary>
    internal MachineRules Rules { get; init; } = MachineRules.None;

    /// <summary>
    /// Whether the machine has server-side behaviour (an event or start gated by a role or a policy, an invoked train,
    /// an effect, a timer, a guard, a reducer that changes the context, a custom guard or reducer, or a start that
    /// reduces its input), so the soft save does not serve it and every change goes through a declared event.
    /// </summary>
    internal bool SaveNotAllowed { get; init; }

    /// <summary>
    /// The machine's effect runner, which <see cref="Send"/> runs the effect through; set by the registry that
    /// built this service. Null for a service built on its own, or a machine with no effect.
    /// </summary>
    public Func<ISnapshotEffectRunner?>? EffectRunner { get; set; }

    /// <inheritdoc/>
    public IReadOnlySet<string>? EndStates { get; set; }

    /// <inheritdoc/>
    public Func<string, Guid, CancellationToken, Task>? EndNotifier { get; set; }

    private static readonly HashSet<string> TriggerNames = new(
        Enum.GetNames<TTrigger>(),
        StringComparer.Ordinal
    );

    /// <summary>
    /// Why <paramref name="actor"/> may not fire <paramref name="trigger"/>, or <see langword="null"/> when it may. Only a client
    /// is checked against a rule; any other actor reaches no user's draft. An unknown trigger is left to the engine,
    /// which answers <c>NO_TRANSITION</c>.
    /// </summary>
    internal async Task<AdvanceOutcome.Rejected?> Refusal(
        Actor actor,
        string trigger,
        CancellationToken cancellationToken
    )
    {
        if (actor is not Actor.Client client || CallerRules.UserOf(actor) is null)
            return CallerRules.Forbidden();
        if (!TriggerNames.Contains(trigger))
            return null;
        if (Rules.Events.TryGetValue(trigger, out var rule))
            return await CallerRules.Admits(client, rule, cancellationToken).ConfigureAwait(false)
                ? null
                : CallerRules.Forbidden();
        // A machine that declares no rule anywhere keeps the authenticated owner's reach it had before rules; one
        // that declares any leaves no event open by omission.
        return Rules.Any ? CallerRules.Forbidden() : null;
    }

    private static AutosaveResult.Rejected SaveRefused() =>
        new(
            RefusalCode.SaveNotAllowed,
            "This machine moves only through its declared events. The generic save serves only machines with no "
                + "server-side behaviour."
        );

    /// <summary>The machine's declared <c>start</c>, or null when it declares none and a client cannot start one.</summary>
    internal StartDeclaration? StartDeclaration { get; init; }

    /// <summary>Whether the machine's instances belong to the system, so no client starts or discards one.</summary>
    internal bool SystemOwned { get; init; }

    /// <summary>
    /// Creates a user's draft in one transaction with its cap and its run. Null for a service built without one,
    /// which cannot start an instance, and creates a saved draft without a cap.
    /// </summary>
    internal InvokeOutbox? Creates { get; init; }

    /// <summary>The most drafts one user may hold, or null for no cap.</summary>
    internal DraftCap? Cap { get; init; }

    /// <summary>
    /// The Trax store, which holds an effect pending on its row and records who put a row in its state; null over a
    /// store a host substituted for it, which keeps the effect-claim ledger and its earlier rules instead.
    /// </summary>
    internal EfSnapshotStore? Marks =>
        store as EfSnapshotStore ?? Invokes?.Store as EfSnapshotStore;

    // The draft's effect keys, each with the state its effect runs from, whether or not a claim ledger is wired.
    internal List<DraftClaimHold.EffectKey> LedgerKeys(string userKey, Guid id)
    {
        if (effectKeysOnReset is null)
            return [];
        var keys = effectKeysOnReset(userKey, id).ToList();
        return effects is { } bound && bound.Count == keys.Count
            ? keys.Select((key, i) => new DraftClaimHold.EffectKey(key, bound[i].From.ToString()))
                .ToList()
            : keys.Select(key => new DraftClaimHold.EffectKey(key, From: null)).ToList();
    }

    private static AdvanceOutcome.Rejected EffectInProgress() =>
        new(
            RefusalCode.EffectInProgress,
            "An action on this instance is still running. Try again once it has finished."
        );

    private static HashSet<string> BuildStateSet(IReadOnlyCollection<TState>? states)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (states is not null)
            foreach (var s in states)
                set.Add(s.ToString()!);
        return set;
    }

    // On returning to the initial state (a reset / "start over") release the effect claims for this instance so
    // the NEXT logical effect can claim a clean key: otherwise it would replay the previous one's receipt and never
    // run. A claim is released only once its outcome is settled on the draft being reset: one still in flight
    // within its lease is kept (the effect may be charging right now), and a completed one is kept unless the
    // draft recorded its receipt, so a receipt that never reached the draft is replayed by the next send instead
    // of the effect running again. With no draft being replaced, a completed claim is an orphan and is released.
    // Written on a token the caller cannot cancel, because the reset it follows has already been written.
    // Idempotent; a no-op when no effects are wired.
    private async Task ReleaseSettledClaims(string userKey, Guid id, string? replacedJson)
    {
        if (effectClaims is null || effectKeysOnReset is null)
            return;
        var recorded = DraftClaimHold.RecordedIn(replacedJson);
        foreach (var key in effectKeysOnReset(userKey, id))
            await effectClaims
                .ReleaseForReset(key, recorded, CancellationToken.None)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels the train run a draft's invoking state queued before the draft is deleted. Null (a service built
    /// without one) cancels nothing, which is right only while no state can have queued a run.
    /// </summary>
    internal IInvokedRunCancellation? RunCancellation { get; init; }

    // The draft's effect keys, each with the state its effect runs from when the keys line up with the machine's
    // effects (they are built from them, in order); otherwise with none, which counts as that state.
    private List<DraftClaimHold.EffectKey> EffectKeysOf(string userKey, Guid id)
    {
        if (effectClaims is null || effectKeysOnReset is null)
            return [];
        var keys = effectKeysOnReset(userKey, id).ToList();
        return effects is { } bound && bound.Count == keys.Count
            ? keys.Select((key, i) => new DraftClaimHold.EffectKey(key, bound[i].From.ToString()))
                .ToList()
            : keys.Select(key => new DraftClaimHold.EffectKey(key, From: null)).ToList();
    }

    // How a discard's delete ended: the draft went, an effect on it is live, or the row changed since it was read.
    private enum DeleteResult
    {
        Deleted,
        EffectLive,
        Changed,
    }

    // A discard over the Trax store: the row holds any effect pending on it, so nothing about the effect is held beside
    // the row but a claim the earlier ledger recorded before the upgrade. One of those in flight within its lease, or a
    // completed one whose draft is still in its effect's state (its receipt not recorded yet), keeps the draft; the next
    // send records it. A run the draft's state invoked is cancelled next, then the row is deleted as it was read, with no
    // pending effect, and the draft's ledger claims go in the delete's transaction.
    private async Task<DeleteResult> DeleteMarkedStore(
        EfSnapshotStore marks,
        string userKey,
        Guid id,
        StoredSnapshot read
    )
    {
        var keys = LedgerKeys(userKey, id);
        var keyList = keys.Select(k => k.Key).ToList();
        if (
            DraftClaimHold.LedgerHolds(
                await marks.LedgerClaims(keyList, CancellationToken.None).ConfigureAwait(false),
                keys,
                StoredState(read.Json)
            )
        )
            return DeleteResult.EffectLive;
        if (read.InvokeToken is { } token && RunCancellation is not null)
            await RunCancellation.Cancel(token, CancellationToken.None).ConfigureAwait(false);
        return await ((IConditionalDraftDelete)marks)
            .DeleteAsRead(userKey, _machineId, id, read, () => marks.ReleaseLedger(keyList))
            .ConfigureAwait(false)
            ? DeleteResult.Deleted
            : DeleteResult.Changed;
    }

    // Every discard deletes through here, so no deleted draft leaves its effect claims, or a live invoked run,
    // behind, and none goes while an effect on it is running. The draft's effect keys are held first, so no effect
    // starts on it while it goes; a key whose effect is in flight, or whose result has not reached the draft yet, is
    // live, and the draft is kept. A run the draft's state invoked is cancelled next: deleting the draft first would
    // leave the run going with no state to deliver its outcome to, and a failed cancel throws, keeping the draft. The
    // delete names the row, revision and invoke token that were read, so a draft written since is kept and read
    // again. Only after the delete, in its transaction, are the held keys and the settled claims released: the draft
    // is gone as a new intent's starting point. None of these writes can be cancelled: a request that goes away
    // between them would otherwise leave claims or a run with no draft, which nothing would read or release again.
    private async Task<DeleteResult> DeleteAsRead(string userKey, Guid id, StoredSnapshot read)
    {
        if (Marks is { } marks && read.RowId is not null && read.Revision is not null)
            return await DeleteMarkedStore(marks, userKey, id, read).ConfigureAwait(false);
        if (
            await DraftClaimHold
                .Take(
                    effectClaims,
                    EffectKeysOf(userKey, id),
                    StoredState(read.Json),
                    DraftClaimHold.RecordedIn(read.Json)
                )
                .ConfigureAwait(false)
            is not { } hold
        )
            return DeleteResult.EffectLive;
        try
        {
            if (read.InvokeToken is { } token && RunCancellation is not null)
                await RunCancellation.Cancel(token, CancellationToken.None).ConfigureAwait(false);

            bool deleted;
            var conditional =
                store as IConditionalDraftDelete ?? Invokes?.Store as IConditionalDraftDelete;
            if (conditional is not null && read.RowId is not null && read.Revision is not null)
                deleted = await conditional
                    .DeleteAsRead(userKey, _machineId, id, read, hold.Release)
                    .ConfigureAwait(false);
            else
            {
                // A store that cannot delete on the row as read deletes by id, once one more read finds the row
                // unchanged; the claims go after it, outside any transaction.
                deleted =
                    await store
                        .Get(userKey, _machineId, id, CancellationToken.None)
                        .ConfigureAwait(false)
                        is { } now
                    && now.Token == read.Token;
                if (deleted && Invokes is { } invokes)
                    deleted = await invokes
                        .Store.DeleteHolding(
                            DraftOwner.User(userKey),
                            _machineId,
                            id,
                            read.InvokeToken,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                else if (deleted)
                    await store
                        .Delete(userKey, _machineId, id, CancellationToken.None)
                        .ConfigureAwait(false);
                if (deleted)
                    await hold.Release().ConfigureAwait(false);
            }
            if (!deleted)
                await hold.Drop().ConfigureAwait(false);
            return deleted ? DeleteResult.Deleted : DeleteResult.Changed;
        }
        catch
        {
            await hold.Drop().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<AutosaveResult> Persisted(
        Written written,
        string userKey,
        Guid id,
        Snapshot snapshot,
        string? replacedJson
    )
    {
        if (!written.Ok)
            return new AutosaveResult.Conflict();
        if (string.Equals(snapshot.State, _initialState, StringComparison.Ordinal))
            await ReleaseSettledClaims(userKey, id, replacedJson).ConfigureAwait(false);
        return new AutosaveResult.Saved(snapshot)
        {
            Revision = written.Revision,
            RowId = written.RowId,
            DueAt = written.DueAt,
        };
    }

    // Creates a saved draft under the per-user cap: the count and the insert are one transaction, under a lock on
    // the user. Insert only, as the uncapped path: a draft created since the read makes this save lose.
    private async Task<AutosaveResult> CreateCapped(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken
    ) =>
        await Creates!
            .Create(
                DraftOwner.User(userKey),
                id,
                snapshot,
                start: null,
                entering: null,
                Cap,
                cancellationToken
            )
            .ConfigureAwait(false) switch
        {
            CreateWrite.Created created => await Persisted(
                    new Written(
                        Ok: true,
                        created.Row.Revision,
                        created.Row.RowId,
                        created.Row.DueAt
                    ),
                    userKey,
                    id,
                    snapshot,
                    replacedJson: null
                )
                .ConfigureAwait(false),
            CreateWrite.Refused refused => new AutosaveResult.Rejected(
                refused.Code,
                refused.Message
            )
            {
                Exception = refused.Exception,
            },
            _ => new AutosaveResult.Conflict(),
        };

    // Whether a write landed, and the revision, row and due time it left where they are known without reading back.
    private readonly record struct Written(
        bool Ok,
        long? Revision,
        long? RowId,
        DateTimeOffset? DueAt = null
    );

    // A blind create or insert-or-update, with the row it left where the store reports it.
    private async Task<Written> Blind(
        string userKey,
        Guid id,
        Snapshot snapshot,
        bool insertOnly,
        CancellationToken cancellationToken
    )
    {
        if (store is IRowReportingSnapshotStore reporting)
        {
            var row = insertOnly
                ? await reporting
                    .Insert(userKey, id, snapshot, cancellationToken)
                    .ConfigureAwait(false)
                : await reporting
                    .Upsert(userKey, id, snapshot, cancellationToken)
                    .ConfigureAwait(false);
            return row is { } written
                ? new Written(Ok: true, written.Revision, written.RowId, written.DueAt)
                : default;
        }
        var ok = insertOnly
            ? await store.Insert(userKey, id, snapshot, cancellationToken).ConfigureAwait(false)
            : await store.Upsert(userKey, id, snapshot, cancellationToken).ConfigureAwait(false);
        return new Written(ok, Revision: null, RowId: null);
    }

    // A write guarded by the token read with stored: it lands on the revision read, so it leaves the next one, and a
    // store that reports it gives the due time the write left. A store that does not reports none.
    private async Task<Written> Swap(
        string userKey,
        Guid id,
        Snapshot snapshot,
        StoredSnapshot stored,
        AppliedRequest? request,
        TransitionNote note,
        CancellationToken cancellationToken
    )
    {
        if (store is IRowReportingSnapshotStore reporting)
            return
                await reporting
                    .UpdateWithRequest(
                        userKey,
                        id,
                        snapshot,
                        stored,
                        request,
                        cancellationToken,
                        note
                    )
                    .ConfigureAwait(false)
                    is { } row
                ? new Written(Ok: true, stored.Revision + 1, stored.RowId, row.DueAt)
                : default;
        var ok = await store
            .UpdateWithRequest(userKey, id, snapshot, stored.Token, request, cancellationToken)
            .ConfigureAwait(false);
        return new Written(ok, stored.Revision + 1, stored.RowId);
    }

    private static AutosaveResult.Saved Unwritten(Snapshot snapshot, StoredSnapshot stored) =>
        new(snapshot)
        {
            Revision = stored.Revision,
            RowId = stored.RowId,
            DueAt = stored.DueAt,
        };

    // Whether the machine itself resets a draft in this state: a transition from it to the initial state.
    private bool MachineResets(string state) =>
        machine.Definition.Transitions.Any(t =>
            string.Equals(t.From.ToString(), state, StringComparison.Ordinal)
            && string.Equals(t.To.ToString(), _initialState, StringComparison.Ordinal)
        );

    // Whether the store keys its drafts by machine. One that implements only the machine-less members keys them
    // by user and id, so a write through it would overwrite another machine's draft under the same id; for such
    // a store a draft this machine cannot see is created with Insert, which refuses when any draft is there.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        Type,
        bool
    > KeysByMachineCache = new();

    private static bool KeysByMachine(ISnapshotStore store) =>
        KeysByMachineCache.GetOrAdd(
            store.GetType(),
            static type =>
            {
                var get = typeof(ISnapshotStore).GetMethod(
                    nameof(ISnapshotStore.Get),
                    [typeof(string), typeof(string), typeof(Guid), typeof(CancellationToken)]
                )!;
                var map = type.GetInterfaceMap(typeof(ISnapshotStore));
                var index = Array.IndexOf(map.InterfaceMethods, get);
                return index >= 0
                    && map.TargetMethods[index].DeclaringType != typeof(ISnapshotStore);
            }
        );

    private static AutosaveResult.Rejected Reserved() =>
        new(
            RefusalCode.StateReserved,
            "This step is completed by its action, not by saving the draft. Send it instead."
        );

    private static AutosaveResult.Rejected Invoking() =>
        new(
            RefusalCode.DraftInvoking,
            "This step is running. It moves on when its work finishes, or through one of its actions."
        );

    /// <inheritdoc/>
    public string Serialize(Snapshot snapshot) => machine.Serialize(snapshot);

    /// <inheritdoc/>
    public async Task<StartOutcome> Start(
        Actor actor,
        JsonObject? input,
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        if (SystemOwned)
            return new StartOutcome.Rejected(
                RefusalCode.SystemOwned,
                "Only the server creates this kind of instance."
            );
        if (Owner(actor) is not { } client)
            return new StartOutcome.Rejected(
                RefusalCode.Unauthenticated,
                "No authenticated user is associated with this request."
            );
        if (StartDeclaration is not { } start)
            return new StartOutcome.Rejected(
                RefusalCode.NoStart,
                $"The machine '{_machineId}' declares no start."
            );
        // A start with no rule admits nobody, as an undeclared event does.
        if (
            Rules.Start is not { } rule
            || !await CallerRules.Admits(client, rule, cancellationToken).ConfigureAwait(false)
        )
            return new StartOutcome.Rejected(RefusalCode.Forbidden, "You may not start this.");
        if (!RequestIdRing.IsUsable(requestId))
            return new StartOutcome.Rejected(
                RefusalCode.InvalidRequestId,
                $"A start needs a request id of 1 to {RequestIdRing.MaxRequestIdLength} characters that is not blank and holds no control character."
            );

        // The input is bounded before it is read, then checked against the start's own record, never the context.
        if (SnapshotLimits.InputTooLarge(input))
            return new StartOutcome.Rejected(
                RefusalCode.TooLarge,
                $"The input exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );
        if (SchemaValidator.ValidateInput(start.InputSchema, input) is { } invalid)
            return new StartOutcome.Rejected(RefusalCode.InvalidInput, invalid);

        // The declared reduction applied to the initial context, validated exactly as a stored snapshot is read back,
        // so nothing is stored that a later read refuses.
        Snapshot initial;
        try
        {
            var definition = machine.Definition;
            var fresh = definition.CreateInitialSnapshot();
            initial = fresh with { Context = start.Apply(fresh.Context, input) };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return new StartOutcome.Rejected(
                RefusalCode.InvalidContext,
                "The start could not build its initial context."
            );
        }
        if (StorableJson.Problem(initial.Context) is { } unstorable)
            return new StartOutcome.Rejected(RefusalCode.Malformed, unstorable);
        var wire = machine.Serialize(initial);
        if (Encoding.UTF8.GetByteCount(wire) > SnapshotLimits.MaxSnapshotBytes)
            return new StartOutcome.Rejected(
                RefusalCode.TooLarge,
                $"The started snapshot exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );
        switch (machine.Rehydrate(wire))
        {
            case RehydrationResult.Ok ok:
                initial = ok.Snapshot;
                break;
            case RehydrationResult.Error error:
                return new StartOutcome.Rejected(RefusalCode.InvalidContext, error.Message);
            default:
                return new StartOutcome.Rejected(
                    RefusalCode.Malformed,
                    "Unknown rehydration result."
                );
        }

        // A started instance enters its initial state: a timed one arms its timer now, with a duration its context
        // must hold within bounds.
        switch (machine.TimerFor(initial.State, initial.Context, restart: true))
        {
            case ({ } timer, _):
                initial = initial with { Timer = timer, ServerRecord = false };
                break;
            case (_, var problem):
                return new StartOutcome.Rejected(RefusalCode.InvalidInput, problem!);
        }

        if (Creates is not { } creates)
            throw new InvalidOperationException(
                $"The machine '{_machineId}' was asked to start an instance, but this draft service was built without "
                    + "the store that creates one. Get the service from ISnapshotMachineRegistry."
            );

        var owner = DraftOwner.User(client.UserKey);
        var request = new StartRequest(requestId, RequestIdRing.Fingerprint("start", input));
        var entering = InvokingNames.Contains(initial.State)
            ? new EnteringInvoke(
                InvokeTrain(initial.State),
                InvokeInput(initial.State),
                InvokedRunLimit
            )
            {
                Spawn = Invoke(initial.State).Spawn,
            }
            : null;

        var write = await creates
            .Create(owner, Guid.NewGuid(), initial, request, entering, Cap, cancellationToken)
            .ConfigureAwait(false);

        // Two starts with one request id that no lock ordered (a store or provider without one) race on the unique
        // index; the loser answers with the winner's instance.
        if (write is CreateWrite.Conflict)
            write = await creates
                .Create(owner, Guid.NewGuid(), initial, request, entering, Cap, cancellationToken)
                .ConfigureAwait(false);

        switch (write)
        {
            case CreateWrite.Created created:
                return new StartOutcome.Started(
                    created.Id,
                    initial,
                    created.Row.Revision,
                    Created: true
                )
                {
                    DueAt = created.Row.DueAt,
                };
            case CreateWrite.Existing existing:
                return machine.Rehydrate(existing.Stored.Json) is RehydrationResult.Ok stored
                    ? new StartOutcome.Started(
                        existing.Id,
                        stored.Snapshot,
                        existing.Stored.Revision ?? 1,
                        Created: false
                    )
                    {
                        DueAt = existing.Stored.DueAt,
                    }
                    : new StartOutcome.Rejected(
                        RefusalCode.Malformed,
                        "The instance this request started can't be read."
                    );
            case CreateWrite.Reused:
                return new StartOutcome.Rejected(
                    RefusalCode.RequestIdReused,
                    "This request id already started an instance with a different input. Send a new id."
                );
            case CreateWrite.Refused refused:
                return new StartOutcome.Rejected(refused.Code, refused.Message);
            default:
                return new StartOutcome.Rejected(
                    RefusalCode.Conflict,
                    "The instance changed elsewhere; retry the start."
                );
        }
    }

    /// <inheritdoc/>
    public async Task<DiscardOutcome> Discard(
        Actor actor,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        if (SystemOwned)
            return new DiscardOutcome.Rejected(
                RefusalCode.SystemOwned,
                "Only the server ends this kind of instance."
            );
        if (Owner(actor) is not { } client)
            return new DiscardOutcome.Rejected(
                RefusalCode.Unauthenticated,
                "No authenticated user is associated with this request."
            );
        if (!RequestIdRing.IsUsable(requestId))
            return new DiscardOutcome.Rejected(
                RefusalCode.InvalidRequestId,
                $"A discard needs a request id of 1 to {RequestIdRing.MaxRequestIdLength} characters that is not blank and holds no control character."
            );

        // The discard rule is checked as an event's is: on a machine that declares rules, a discard without one
        // admits nobody; on a machine that declares none, the owner may discard.
        if (
            Rules.Discard is { } rule
                ? !await CallerRules.Admits(client, rule, cancellationToken).ConfigureAwait(false)
                : Rules.Any
        )
            return new DiscardOutcome.Rejected(
                RefusalCode.Forbidden,
                "You are not allowed to discard this instance."
            );

        // Someone else's id reads as no draft at all, and is answered exactly as one that is already gone. An
        // instance in a state only the server puts it in (committed, an effect's target, invoking, an outcome's
        // target, a gated event's target) is the record of what the server did, and stays; so does one an effect is
        // running on. The delete is conditional on the row as it was checked: a draft written in between is not
        // deleted, and is read and checked again.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stored = await store
                .Get(client.UserKey, _machineId, id, cancellationToken)
                .ConfigureAwait(false);
            if (stored is null)
                return new DiscardOutcome.Discarded();
            if (stored.Effect is not null)
                return new DiscardOutcome.Rejected(
                    RefusalCode.DiscardNotAllowed,
                    "An effect on this instance is running, so the instance is becoming the record of what the "
                        + "server did, and cannot be discarded."
                );
            if (stored.ParentWaits)
                return new DiscardOutcome.Rejected(
                    RefusalCode.DiscardNotAllowed,
                    "This instance belongs to the step that started it, which is waiting for it to finish, so it "
                        + "cannot be discarded."
                );
            if (StoredState(stored.Json) is { } state && KeptRecord(stored, state))
                return new DiscardOutcome.Rejected(
                    RefusalCode.DiscardNotAllowed,
                    $"An instance in {state} is the record of what the server did, and cannot be discarded."
                );
            switch (await DeleteAsRead(client.UserKey, id, stored).ConfigureAwait(false))
            {
                case DeleteResult.Deleted:
                    return new DiscardOutcome.Discarded();
                case DeleteResult.EffectLive:
                    return new DiscardOutcome.Rejected(
                        RefusalCode.DiscardNotAllowed,
                        "An effect on this instance is running or recording its result, so the instance is "
                            + "becoming the record of what the server did, and cannot be discarded."
                    );
            }
        }
        return
            await store.Get(client.UserKey, _machineId, id, cancellationToken).ConfigureAwait(false)
                is null
            ? new DiscardOutcome.Discarded()
            : new DiscardOutcome.Rejected(
                RefusalCode.Conflict,
                "The draft changed while it was being discarded; retry."
            );
    }

    // Whether a row is the record of what the server did: one waiting on the run its state invoked, or one the server put
    // in its state. A row that does not record who put it there is judged by its state, as before the record existed.
    private bool KeptRecord(StoredSnapshot stored, string state) =>
        InvokingNames.Contains(state) || (stored.ServerRecord ?? ReservedNames.Contains(state));

    // The state a stored row names, read without rehydrating it, or null when the row is not a snapshot at all (which
    // its owner may discard: nothing can be read from it).
    private static string? StoredState(string json)
    {
        try
        {
            return
                JsonNode.Parse(json) is JsonObject { } row
                && row["state"] is JsonValue value
                && value.TryGetValue<string>(out var state)
                ? state
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The client actor, or null when it names no user: an empty or whitespace key names no owner.
    private static Actor.Client? Owner(Actor actor) =>
        CallerRules.UserOf(actor) is not null ? (Actor.Client)actor : null;

    /// <inheritdoc/>
    public async Task<LoadResult> Load(
        Actor actor,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        // A system-owned instance has no client reader, and a caller the read rule refuses is told nothing more
        // than an unknown id would tell it.
        if (SystemOwned || Owner(actor) is not { } client)
            return new LoadResult.NotFound();
        if (!await CallerRules.MayRead(client, Rules, cancellationToken).ConfigureAwait(false))
            return new LoadResult.NotFound();
        return await Load(client.UserKey, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The row id of the caller's instance <paramref name="id"/>, the incarnation a the push (<c>&lt;machine&gt;Changed</c>) stream
    /// follows, when the machine's watch rule admits the caller; null for anything else, alike: no such instance,
    /// another user's, a system instance, an actor that names no user, a caller the rule refuses, and a machine that
    /// declares no watch rule. It reads the row once and rehydrates nothing: the stream's first event is the row as it
    /// is read after the watch registers, so nothing written in between is lost.
    /// </summary>
    public async Task<long?> AuthorizeWatch(
        Actor actor,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        if (SystemOwned || Owner(actor) is not { } client)
            return null;
        if (
            Rules.Watch is not { } rule
            || !await CallerRules.Admits(client, rule, cancellationToken).ConfigureAwait(false)
        )
            return null;
        var stored = await store
            .Get(client.UserKey, _machineId, id, cancellationToken)
            .ConfigureAwait(false);
        return stored?.RowId;
    }

    /// <summary>Read the caller's draft, validating the stored data on the way out.</summary>
    public async Task<LoadResult> Load(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        SnapshotOwner.Require(userKey);
        var stored = await store
            .Get(userKey, _machineId, id, cancellationToken)
            .ConfigureAwait(false);
        if (stored is null)
            return new LoadResult.NotFound();

        // A read is a query: it never deletes. Expiry belongs to the draft sweep, which deletes a draft idle past
        // the TTL conditionally on the revision it read.
        return machine.Rehydrate(stored.Json) switch
        {
            RehydrationResult.Ok ok => new LoadResult.Loaded(ok.Snapshot)
            {
                Token = stored.Token,
                Revision = stored.Revision,
                RowId = stored.RowId,
                DueAt = stored.DueAt,
                Effect = stored.Effect,
            },
            RehydrationResult.Error error => new LoadResult.Invalid(error.Code, error.Message)
            {
                Exception = error.Exception,
            },
            _ => new LoadResult.Invalid(RefusalCode.Malformed, "Unknown rehydration result."),
        };
    }

    /// <summary>Soft path: validate a client-provided snapshot and persist it. Invalid data is never stored.</summary>
    public async Task<AutosaveResult> Autosave(
        string userKey,
        Guid id,
        string snapshotJson,
        CancellationToken cancellationToken = default
    )
    {
        SnapshotOwner.Require(userKey);

        // A machine whose server does anything a client cannot (an event or start gated by a role or a policy, an
        // invoked train, an effect, a timer, a guard or a reducer) is moved by its declared events only.
        if (SaveNotAllowed)
            return SaveRefused();

        // Bound the payload before any parsing or DB work (DoS guard).
        if (Encoding.UTF8.GetByteCount(snapshotJson) > SnapshotLimits.MaxSnapshotBytes)
            return new AutosaveResult.Rejected(
                RefusalCode.TooLarge,
                $"Snapshot exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        switch (machine.Rehydrate(snapshotJson))
        {
            case RehydrationResult.Ok saved:
                // A saved draft in a timed state keeps the due time it was given at entry, or arms one when it is
                // created or moved there; a duration its context cannot give is refused, never stored.
                if (
                    machine.TimerFor(saved.Snapshot.State, saved.Snapshot.Context, restart: false)
                    is not
                    ({ } savedTimer, _)
                )
                    return new AutosaveResult.Rejected(
                        RefusalCode.InvalidInput,
                        machine
                            .TimerFor(saved.Snapshot.State, saved.Snapshot.Context, restart: false)
                            .Problem!
                    );
                var ok = new RehydrationResult.Ok(
                    saved.Snapshot with
                    {
                        Timer = savedTimer,
                        ServerRecord = false,
                    }
                );

                // Fast path: nothing to protect => blind last-writer-wins autosave. A store that does not key by
                // machine is read first, so a draft of another machine under this id is never overwritten.
                if (ReservedNames.Count == 0)
                {
                    // A capped save that creates a draft counts the user's drafts in the same transaction.
                    if (
                        Cap is not null
                        && Creates is not null
                        && await store
                            .Get(userKey, _machineId, id, cancellationToken)
                            .ConfigureAwait(false)
                            is null
                    )
                        return await CreateCapped(userKey, id, ok.Snapshot, cancellationToken)
                            .ConfigureAwait(false);

                    var upserted = await Blind(
                            userKey,
                            id,
                            ok.Snapshot,
                            insertOnly: !KeysByMachine(store)
                                && await store
                                    .Get(userKey, _machineId, id, cancellationToken)
                                    .ConfigureAwait(false)
                                    is null,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    return await Persisted(upserted, userKey, id, ok.Snapshot, replacedJson: null)
                        .ConfigureAwait(false);
                }

                // Guarded path. A soft save never creates a committed state: it may not create a draft in, or
                // move one into, a committed state or an effect's target, because only the effect runner puts a
                // draft there. Nor may it rewrite a draft already there: the receipt such a draft holds is the one
                // the effect produced for exactly that content, so the only write allowed is a reset the machine
                // itself declares, and a save identical to what is stored is answered without writing.
                var stored = await store
                    .Get(userKey, _machineId, id, cancellationToken)
                    .ConfigureAwait(false);
                var entering = ReservedNames.Contains(ok.Snapshot.State);
                if (stored is null)
                {
                    if (entering)
                        return Reserved();
                    if (Cap is not null && Creates is not null)
                        return await CreateCapped(userKey, id, ok.Snapshot, cancellationToken)
                            .ConfigureAwait(false);
                    // Insert, never Upsert: a draft created since the read above (and sent, perhaps) must make
                    // this save lose rather than be overwritten by it.
                    var inserted = await Blind(
                            userKey,
                            id,
                            ok.Snapshot,
                            insertOnly: true,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    return await Persisted(inserted, userKey, id, ok.Snapshot, replacedJson: null)
                        .ConfigureAwait(false);
                }

                var readable = machine.Rehydrate(stored.Json) as RehydrationResult.Ok;
                var unchanged =
                    readable is not null
                    && string.Equals(
                        machine.Serialize(ok.Snapshot),
                        machine.Serialize(readable.Snapshot),
                        StringComparison.Ordinal
                    );

                // A draft whose state invoked a train holds that run: the soft path neither leaves the state, which
                // would strand the run, nor rewrites the context it was started from. Only a declared transition
                // or the run's outcome moves it on. Decided by the token as well as the state, so an unreadable
                // draft holding a run is not reset from under it either.
                if (
                    stored.InvokeToken is not null
                    || (readable is not null && InvokingNames.Contains(readable.Snapshot.State))
                )
                    return unchanged ? Unwritten(readable!.Snapshot, stored) : Invoking();

                // A stored draft that cannot be read may be committed, so nothing but a reset overwrites it.
                if (readable is not { } current)
                {
                    if (!string.Equals(ok.Snapshot.State, _initialState, StringComparison.Ordinal))
                        return new AutosaveResult.Rejected(
                            RefusalCode.DraftUnreadable,
                            "The saved draft can't be read, so it can only be started over."
                        );
                }
                else if (LockedNames.Contains(current.Snapshot.State))
                {
                    if (unchanged)
                        return Unwritten(current.Snapshot, stored);
                    if (
                        !string.Equals(ok.Snapshot.State, _initialState, StringComparison.Ordinal)
                        || !MachineResets(current.Snapshot.State)
                    )
                        return new AutosaveResult.Rejected(
                            RefusalCode.DraftCommitted,
                            "This draft was already completed and can't be overwritten by an edit."
                        );
                }
                else if (entering)
                    return unchanged ? Unwritten(current.Snapshot, stored) : Reserved();

                // Atomic overwrite guarded by the token we just read: a commit that lands between this read
                // and this write makes the soft save LOSE (Conflict) instead of resurrecting the draft.
                var wrote = await Swap(
                        userKey,
                        id,
                        ok.Snapshot,
                        stored,
                        stored.LastRequest,
                        TransitionNote.Save(DraftOwner.User(userKey)),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return await Persisted(wrote, userKey, id, ok.Snapshot, stored.Json)
                    .ConfigureAwait(false);

            case RehydrationResult.Error error:
                return new AutosaveResult.Rejected(error.Code, error.Message)
                {
                    Exception = error.Exception,
                };
            default:
                return new AutosaveResult.Rejected(
                    RefusalCode.Malformed,
                    "Unknown rehydration result."
                );
        }
    }

    /// <summary>
    /// Authoritative path: read the STORED snapshot, re-drive it by one trigger, and persist the result
    /// with an optimistic-concurrency check — never trusting a client-computed state. Pass a stable
    /// <paramref name="requestId"/> to make retries idempotent: a repeat of the same request (the same id
    /// and trigger) returns the current snapshot instead of firing the trigger again, and an id reused for
    /// a different trigger is refused as <c>REQUEST_ID_REUSED</c>. A result that could not be stored, or is
    /// larger than <see cref="SnapshotLimits.MaxSnapshotBytes"/>, is refused before anything is written.
    /// </summary>
    public Task<AdvanceOutcome> Advance(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input = null,
        string? requestId = null,
        CancellationToken cancellationToken = default
    ) => Advance(userKey, id, trigger, input, requestId, clientResult: null, cancellationToken);

    /// <inheritdoc cref="ISnapshotDraftService.Advance(string, Guid, string, JsonNode?, string?, string?, CancellationToken)"/>
    public Task<AdvanceOutcome> Advance(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        string? clientResult,
        CancellationToken cancellationToken = default
    ) =>
        AdvanceCore(
            userKey,
            id,
            trigger,
            input,
            requestId,
            clientResult,
            new Actor.Client(userKey),
            effectRan: false,
            expectedToken: null,
            cancellationToken
        );

    /// <inheritdoc/>
    public Task<AdvanceOutcome> Advance(
        Actor actor,
        Guid id,
        string trigger,
        JsonNode? input = null,
        string? requestId = null,
        CancellationToken cancellationToken = default
    ) => Advance(actor, id, trigger, input, requestId, clientResult: null, cancellationToken);

    /// <inheritdoc/>
    public async Task<AdvanceOutcome> Advance(
        Actor actor,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        string? clientResult,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (CallerRules.UserOf(actor) is not { } userKey)
            return CallerRules.Forbidden();
        return await AdvanceCore(
                userKey,
                id,
                trigger,
                input,
                requestId,
                clientResult,
                actor,
                effectRan: false,
                expectedToken: null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<AdvanceOutcome> Send(
        Actor actor,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (CallerRules.UserOf(actor) is null)
            return CallerRules.Forbidden();
        if (effects is not { Count: > 0 })
            return new AdvanceOutcome.Rejected(
                RefusalCode.NoEffect,
                $"Machine '{_machineId}' has no irreversible effect to send."
            );
        var runner =
            EffectRunner?.Invoke() as IActorEffectRunner
            ?? throw new InvalidOperationException(
                $"The machine '{_machineId}' runs its effect through the runner its registry builds, and this draft "
                    + "service was built on its own. Get the service from ISnapshotMachineRegistry."
            );
        return await runner.Run(actor, id, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The effect runner's advance: fires <paramref name="trigger"/> even when it is bound to the machine's
    /// effect, because the runner calls it only after the effect has run and carries the effect's receipt in
    /// <paramref name="input"/>. It writes only while the draft still carries <paramref name="expectedToken"/>,
    /// the token of the load the effect ran on, so the receipt is never recorded on content the effect did not
    /// act on; a draft written since is a <see cref="AdvanceOutcome.Conflict"/>, and the claim keeps the receipt
    /// for the next send to replay. Every other check of the public advance still applies.
    /// </summary>
    internal Task<AdvanceOutcome> AdvanceAfterEffect(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        Guid expectedToken,
        CancellationToken cancellationToken
    ) =>
        AdvanceCore(
            userKey,
            id,
            trigger,
            input,
            requestId,
            clientResult: null,
            actor: null,
            effectRan: true,
            expectedToken,
            cancellationToken
        );

    /// <summary>
    /// Records the effect <paramref name="effectOwner"/> claimed on the draft's row: fires <paramref name="trigger"/>
    /// with the effect's receipt in <paramref name="input"/>, and writes the result, the request and the end of the
    /// pending mark in one write that matches only while the effect is still that claimant's. A
    /// <see cref="AdvanceOutcome.Conflict"/> when another claimant took the effect over.
    /// </summary>
    internal Task<AdvanceOutcome> RecordEffect(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        Guid effectOwner
    ) =>
        AdvanceCore(
            userKey,
            id,
            trigger,
            input,
            requestId,
            clientResult: null,
            actor: null,
            effectRan: true,
            expectedToken: null,
            CancellationToken.None,
            effectOwner
        );

    // actor is null only after the effect ran: the runner checked the event's rule before it claimed the effect, and
    // refusing now would leave an effect that happened unrecorded.
    // Every advance ends here. One that leaves the instance in a state it ends in, when a parent spawned it, tells the
    // parent at once; the reconciler's sweep delivers it otherwise.
    private async Task<AdvanceOutcome> AdvanceCore(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        string? clientResult,
        Actor? actor,
        bool effectRan,
        Guid? expectedToken,
        CancellationToken cancellationToken,
        Guid? effectOwner = null
    )
    {
        var outcome = await AdvanceAndAnswer(
                userKey,
                id,
                trigger,
                input,
                requestId,
                clientResult,
                actor,
                effectRan,
                expectedToken,
                cancellationToken,
                effectOwner
            )
            .ConfigureAwait(false);
        if (
            outcome is AdvanceOutcome.Advanced advanced
            && EndStates is { } ends
            && ends.Contains(advanced.Snapshot.State)
            && EndNotifier is { } notify
        )
            await notify(userKey, id, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    private async Task<AdvanceOutcome> AdvanceAndAnswer(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        string? clientResult,
        Actor? actor,
        bool effectRan,
        Guid? expectedToken,
        CancellationToken cancellationToken,
        Guid? effectOwner = null
    )
    {
        var outcome = await AdvanceOnce(
                userKey,
                id,
                trigger,
                input,
                requestId,
                clientResult,
                actor,
                effectRan,
                expectedToken,
                replayOnly: false,
                cancellationToken,
                effectOwner: effectOwner
            )
            .ConfigureAwait(false);
        if (effectOwner is not null)
            return outcome;

        // Two copies of one request racing each other: the one that lost the write finds the row the winner wrote,
        // with this request id in its ring, and answers with the result recorded there rather than a conflict. A row
        // another request wrote is still a conflict: this request is never fired on a state its client did not see.
        if (outcome is AdvanceOutcome.Conflict && requestId is not null && expectedToken is null)
            outcome = await AdvanceOnce(
                    userKey,
                    id,
                    trigger,
                    input,
                    requestId,
                    clientResult,
                    actor,
                    effectRan,
                    expectedToken,
                    replayOnly: true,
                    cancellationToken
                )
                .ConfigureAwait(false);

        // Another write landed between this request's read and its write: a timer that fired, an outcome, another
        // client. The request is read once more against what is stored now, and answered only if it is refused there
        // (an event the new state has no edge for is a no-transition, as the server's snapshot says); one that would
        // still move the instance stays a conflict, because a request is never fired on a state its client did not
        // see.
        if (outcome is AdvanceOutcome.Conflict && expectedToken is null)
            outcome = await AdvanceOnce(
                    userKey,
                    id,
                    trigger,
                    input,
                    requestId,
                    clientResult,
                    actor,
                    effectRan,
                    expectedToken,
                    replayOnly: false,
                    cancellationToken,
                    refusalOnly: true
                )
                .ConfigureAwait(false);
        return outcome;
    }

    private async Task<AdvanceOutcome> AdvanceOnce(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        string? clientResult,
        Actor? actor,
        bool effectRan,
        Guid? expectedToken,
        bool replayOnly,
        CancellationToken cancellationToken,
        bool refusalOnly = false,
        Guid? effectOwner = null
    )
    {
        SnapshotOwner.Require(userKey);

        // A timer trigger is the server acting once a state has been held long enough: no caller fires it, whoever
        // they are, and it is refused before the draft, the caller or the request is read.
        if (machine.IsTimerTrigger(trigger))
            return new AdvanceOutcome.Rejected(
                RefusalCode.TimerBound,
                "This step moves on by itself when its time is up, not by an action."
            );

        // A request id is recorded on the row, so one that could not be (too long, holding a character the database
        // cannot store, or blank, which names no request) is refused before anything is read, as start and discard
        // refuse it.
        if (requestId is not null && !RequestIdRing.IsUsable(requestId))
            return new AdvanceOutcome.Rejected(
                RefusalCode.InvalidRequestId,
                $"A request id is 1 to {RequestIdRing.MaxRequestIdLength} characters, not blank, with no control character."
            );

        // An oversized input is refused before it is validated or the draft is read, on this path as on the
        // generic junction.
        if (SnapshotLimits.InputTooLarge(input))
            return new AdvanceOutcome.Rejected(
                RefusalCode.TooLarge,
                $"The input exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        var stored = await store
            .Get(userKey, _machineId, id, cancellationToken)
            .ConfigureAwait(false);
        if (stored is null)
            return new AdvanceOutcome.NotFound();

        // The effect's record lands only while the effect is still this claimant's: one taken over is the new
        // claimant's to record.
        if (effectOwner is { } claimant)
        {
            if (stored.Effect?.Token != claimant)
                return new AdvanceOutcome.Conflict();
        }
        // The draft changed after the effect's load: whatever is stored now is not what the effect acted on.
        else if (expectedToken is { } expected && stored.Token != expected)
            return new AdvanceOutcome.Conflict();

        Snapshot current;
        switch (machine.Rehydrate(stored.Json))
        {
            case RehydrationResult.Ok ok:
                current = ok.Snapshot;
                break;
            case RehydrationResult.Error error:
                return new AdvanceOutcome.LoadError(error.Code, error.Message)
                {
                    Exception = error.Exception,
                };
            default:
                return new AdvanceOutcome.LoadError(
                    RefusalCode.Malformed,
                    "Unknown rehydration result."
                );
        }

        // An effect-bound edge records the effect's receipt. Fired from here, the receipt would be whatever the
        // caller put in the input and the effect would never have run, so only the effect runner fires it.
        if (!effectRan && _effectEdges.Contains((current.State, trigger)))
            return new AdvanceOutcome.Rejected(
                RefusalCode.EffectBound,
                "This action runs an irreversible effect. Send it instead of advancing."
            );

        // An outcome trigger records what an invoked train produced. Fired from here, its input would be whatever
        // the caller sent and no run would have produced it, so only the run's outcome applies it.
        if (machine.IsOutcomeTrigger(trigger))
            return new AdvanceOutcome.Rejected(
                RefusalCode.OutcomeBound,
                "This step is completed by the outcome of its work, not by an action."
            );

        // The caller is told whether they may send the event before anything about the request: a retry is checked
        // against the rule as it is now, so a caller who lost the role is refused rather than replayed.
        if (
            actor is not null
            && await Refusal(actor, trigger, cancellationToken).ConfigureAwait(false) is { } refused
        )
            return refused;

        // The request as the client sent it: the effect runner's receipt is the server's input, not the client's.
        var fingerprint = requestId is null
            ? null
            : RequestIdRing.Fingerprint(trigger, effectRan ? null : input);
        var retry = Retry(stored, requestId, fingerprint, trigger);
        if (replayOnly && retry.Kind != RetryKind.Replay)
            return new AdvanceOutcome.Conflict();
        switch (retry.Kind)
        {
            case RetryKind.Replay:
                return Replayed(retry.Recorded, current, stored, clientResult);
            case RetryKind.Reused:
                return new AdvanceOutcome.Rejected(
                    RefusalCode.RequestIdReused,
                    "This request id was already used for a different action. Send a new id."
                );
        }

        // A row with a pending effect is written by that effect only: every other event waits for it to be recorded.
        if (effectOwner is null && stored.Effect is not null)
            return EffectInProgress();

        // Compute the advance and check it in full before anything is written, so a refusal below leaves the
        // stored draft exactly as it was.
        Snapshot next;
        switch (machine.Advance(current, trigger, input))
        {
            case AdvanceResult.Rejected rejected:
                return new AdvanceOutcome.Rejected(rejected.Reason, rejected.Detail)
                {
                    Exception = rejected.Exception,
                };
            case AdvanceResult.Transitioned when refusalOnly:
                return new AdvanceOutcome.Conflict();
            case AdvanceResult.Transitioned transitioned:
                next = transitioned.Snapshot;
                break;
            default:
                return new AdvanceOutcome.Rejected(RefusalCode.InternalError, Detail: null);
        }

        if (StorableJson.Problem(next.Context) is { } unstorable)
            return new AdvanceOutcome.Rejected(RefusalCode.Malformed, unstorable);

        var wire = machine.Serialize(next);
        if (Encoding.UTF8.GetByteCount(wire) > SnapshotLimits.MaxSnapshotBytes)
            return new AdvanceOutcome.Rejected(
                RefusalCode.TooLarge,
                $"The advanced snapshot exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        if (Checked(wire, clientResult) is { } divergence)
            return divergence;

        // The row records whether the server put it in its state: an effect's record, or an event gated by a role or a
        // policy, is the server's; any other event is the client's.
        next = next with
        {
            ServerRecord =
                effectRan
                || (Rules.Events.TryGetValue(trigger, out var eventRule) && eventRule.Gated),
        };

        if (effectOwner is { } owner)
        {
            var recorded = await Marks!
                .CompleteEffect(
                    DraftOwner.User(userKey),
                    id,
                    next,
                    stored,
                    Recorded(stored, requestId, fingerprint, trigger, current.State, wire),
                    owner,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            return recorded is { } row
                ? Advanced(next, stored, row.DueAt)
                : new AdvanceOutcome.Conflict();
        }

        // Entering or leaving an invoking state writes the snapshot together with the run it queues or cancels.
        // A self-loop neither leaves nor enters: the state keeps its run.
        var leaving =
            InvokingNames.Contains(current.State)
            && !string.Equals(next.State, current.State, StringComparison.Ordinal);
        var enteringInvoke =
            InvokingNames.Contains(next.State)
            && !string.Equals(next.State, current.State, StringComparison.Ordinal);
        if (leaving || enteringInvoke)
            return await AdvanceInvoking(
                    userKey,
                    id,
                    current,
                    next,
                    stored,
                    Recorded(stored, requestId, fingerprint, trigger, current.State, wire),
                    leaving,
                    enteringInvoke,
                    trigger,
                    cancellationToken
                )
                .ConfigureAwait(false);

        var updated = await Swap(
                userKey,
                id,
                next,
                stored,
                Recorded(stored, requestId, fingerprint, trigger, current.State, wire),
                TransitionNote.Command(DraftOwner.User(userKey), trigger),
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!updated.Ok)
            return new AdvanceOutcome.Conflict();
        if (string.Equals(next.State, _initialState, StringComparison.Ordinal) && Marks is null)
            await ReleaseSettledClaims(userKey, id, stored.Json).ConfigureAwait(false);
        return Advanced(next, stored, updated.DueAt);
    }

    // The outcome of a write guarded by the token read with stored: it landed on the revision read, so it reports
    // the next one, and the due time the write left, without reading the row back.
    private static AdvanceOutcome.Advanced Advanced(
        Snapshot next,
        StoredSnapshot stored,
        DateTimeOffset? dueAt
    ) =>
        new(next)
        {
            Revision = stored.Revision + 1,
            RowId = stored.RowId,
            DueAt = dueAt,
        };

    // The outbox write of an advance that enters or leaves an invoking state: the snapshot, the old run's cancel and
    // the new run's work queue entry commit together or not at all.
    private async Task<AdvanceOutcome> AdvanceInvoking(
        string userKey,
        Guid id,
        Snapshot current,
        Snapshot next,
        StoredSnapshot stored,
        AppliedRequest? request,
        bool leaving,
        bool entering,
        string trigger,
        CancellationToken cancellationToken
    )
    {
        var invokes =
            Invokes
            ?? throw new InvalidOperationException(
                $"The machine '{_machineId}' invokes a train in {(entering ? next.State : current.State)}, but this "
                    + "draft service was built without the invoke outbox, so it cannot queue or cancel the run. "
                    + "Get the service from ISnapshotMachineRegistry."
            );

        var write = await invokes
            .Outbox.Advance(
                DraftOwner.User(userKey),
                id,
                next,
                stored.Token,
                request,
                leaving ? stored.InvokeToken : null,
                entering
                    ? new EnteringInvoke(
                        InvokeTrain(next.State),
                        InvokeInput(next.State),
                        InvokedRunLimit
                    )
                    {
                        Spawn = Invoke(next.State).Spawn,
                    }
                    : null,
                cancellationToken,
                trigger
            )
            .ConfigureAwait(false);

        switch (write)
        {
            case InvokeWrite.Written written:
                if (
                    string.Equals(next.State, _initialState, StringComparison.Ordinal)
                    && Marks is null
                )
                    await ReleaseSettledClaims(userKey, id, stored.Json).ConfigureAwait(false);
                return Advanced(next, stored, written.DueAt);
            case InvokeWrite.Refused refused:
                return new AdvanceOutcome.Rejected(refused.Code, refused.Message)
                {
                    Exception = refused.Exception,
                };
            default:
                return new AdvanceOutcome.Conflict();
        }
    }

    private InvokeDefinition<TState> Invoke(string state) =>
        machine.Definition.Invokes.Values.First(i =>
            string.Equals(i.State.ToString(), state, StringComparison.Ordinal)
        );

    private Type InvokeTrain(string state) => Invoke(state).TrainType;

    private Func<JsonObject, string?, object?> InvokeInput(string state) =>
        Invoke(state).CreateInput;

    /// <summary>
    /// Whether the draft is still exactly as <paramref name="loaded"/> read it. The effect runner asks after it has
    /// claimed the effect and before it runs it: a timer that fired in between, or any other write, means the effect
    /// would act on a state the instance has left. On the EF store over Postgres the read waits for a transaction
    /// writing the row (a timer's fire holds it locked) and reads what it left; another store compares the token.
    /// </summary>
    internal async Task<bool> StillAsLoaded(
        string userKey,
        Guid id,
        LoadResult.Loaded loaded,
        CancellationToken cancellationToken
    )
    {
        if (store is EfSnapshotStore ef && loaded is { RowId: { } rowId, Revision: { } revision })
            return await ef.StillAt(rowId, revision, cancellationToken).ConfigureAwait(false);
        return await store.Get(userKey, _machineId, id, cancellationToken).ConfigureAwait(false)
                is { } stored
            && stored.Token == loaded.Token;
    }

    /// <summary>
    /// What the draft already answers <paramref name="requestId"/> with, for an advance on <paramref name="trigger"/>
    /// with no client input: the recorded result when the draft recorded that request (a retry replays it), a
    /// <c>REQUEST_ID_REUSED</c> refusal when it recorded the id for a different request, or null when the request is
    /// new. The effect runner asks before it runs an irreversible effect, so a retried send never runs it again and a
    /// refused id never pays for a delivery it cannot record.
    /// </summary>
    internal async Task<AdvanceOutcome?> RecordedAnswer(
        string userKey,
        Guid id,
        string requestId,
        string trigger,
        Snapshot current,
        CancellationToken cancellationToken
    )
    {
        if (
            await store.Get(userKey, _machineId, id, cancellationToken).ConfigureAwait(false)
            is not { } stored
        )
            return null;
        var retry = Retry(
            stored,
            requestId,
            RequestIdRing.Fingerprint(trigger, input: null),
            trigger
        );
        return retry.Kind switch
        {
            RetryKind.Replay => Replayed(retry.Recorded, current, stored, clientResult: null),
            RetryKind.Reused => new AdvanceOutcome.Rejected(
                RefusalCode.RequestIdReused,
                "This request id was already used for a different action. Send a new id."
            ),
            _ => null,
        };
    }

    private enum RetryKind
    {
        /// <summary>Not a retry: fire the trigger.</summary>
        Fire,

        /// <summary>A retry of a recorded request: answer with its recorded result.</summary>
        Replay,

        /// <summary>The recorded id with a different request (or none recorded): refuse.</summary>
        Reused,
    }

    // How a request is answered, and the recorded request a replay answers with (null on a row written before the
    // ring, which replays the current snapshot).
    private readonly record struct RetryDecision(RetryKind Kind, RecordedRequest? Recorded = null);

    // Whether this request repeats one the row recorded. The id alone is not enough: a client can reuse an id for a
    // different trigger or input, and replaying then answers one action with the outcome of another, so the same
    // id with a different fingerprint is refused. A repeat of a recorded request always replays the result recorded
    // for it, wherever the row is now: a timer, an outcome or any other write that brought the row back to the state
    // the request fired from does not make the client's one request a second one. An id the ring no longer holds,
    // or never held, is a new request.
    private static RetryDecision Retry(
        StoredSnapshot stored,
        string? requestId,
        string? fingerprint,
        string trigger
    )
    {
        if (requestId is null || fingerprint is null)
            return new(RetryKind.Fire);

        if (stored.RecentRequests is not { } ring)
            return new(LegacyRetry(stored.LastRequest, requestId, trigger));

        var index = -1;
        for (var i = ring.Count - 1; i >= 0; i--)
            if (string.Equals(ring[i].RequestId, requestId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        if (index < 0)
            return new(RetryKind.Fire);

        var recorded = ring[index];
        return string.Equals(recorded.Fingerprint, fingerprint, StringComparison.Ordinal)
            ? new(RetryKind.Replay, recorded)
            : new(RetryKind.Reused);
    }

    // A row written before it kept a ring records its last request only: a matching id and trigger replay the
    // current snapshot.
    private static RetryKind LegacyRetry(AppliedRequest? last, string requestId, string trigger)
    {
        if (last is null || !string.Equals(requestId, last.RequestId, StringComparison.Ordinal))
            return RetryKind.Fire;
        if (
            !string.Equals(last.Trigger, trigger, StringComparison.Ordinal)
            || last.FromState is null
        )
            return RetryKind.Reused;
        return RetryKind.Replay;
    }

    // The request this advance records, with the row's ring and it as the newest entry; null without a request id.
    private static AppliedRequest? Recorded(
        StoredSnapshot stored,
        string? requestId,
        string? fingerprint,
        string trigger,
        string fromState,
        string resultWire
    ) =>
        requestId is null || fingerprint is null
            ? null
            : new AppliedRequest(requestId, trigger, fromState)
            {
                Ring = RequestIdRing.With(
                    stored.RecentRequests,
                    new RecordedRequest(
                        requestId,
                        fingerprint,
                        (stored.Revision ?? 0) + 1,
                        resultWire
                    )
                    {
                        Trigger = trigger,
                        FromState = fromState,
                    }
                ),
            };

    // A replay answers with the result recorded for the request, judged against the client's own result for it.
    // A row written before the ring records no result, and replays the current snapshot.
    private AdvanceOutcome Replayed(
        RecordedRequest? recorded,
        Snapshot current,
        StoredSnapshot stored,
        string? clientResult
    )
    {
        if (recorded is null)
            return Checked(current, clientResult)
                ?? new AdvanceOutcome.Advanced(current)
                {
                    Revision = stored.Revision,
                    RowId = stored.RowId,
                    DueAt = stored.DueAt,
                };

        if (Checked(recorded.ResultSnapshot, clientResult) is { } divergence)
            return divergence;
        return machine.Rehydrate(recorded.ResultSnapshot) is RehydrationResult.Ok ok
            ? new AdvanceOutcome.Advanced(ok.Snapshot)
            {
                Revision = recorded.Revision > 0 ? recorded.Revision : null,
                RowId = stored.RowId,
                // The ring records the result, not its due time: the row's is the answer's only while the row is
                // still at the recorded revision. An older answer says its due time is unknown rather than null, which
                // a client would read as a timer the server cleared; a client holding a newer revision ignores it.
                DueAt = recorded.Revision == stored.Revision ? stored.DueAt : null,
                DueAtKnown = recorded.Revision == stored.Revision,
            }
            // A recorded result the current definition cannot read is answered as a stale view: the client reloads.
            : new AdvanceOutcome.Conflict();
    }

    // A client that rebuilds its prediction from a typed answer cannot tell a field stored as null from one not
    // stored at all, nor keep the canonical key order, so the prediction also matches when, written canonically with
    // every null member left out, it is the server's result written the same way. Anything else that differs is a
    // divergence, as is a prediction that is not JSON.
    private static bool SameSnapshot(string serverWire, string clientResult)
    {
        try
        {
            return string.Equals(
                CanonicalJson.Serialize(WithoutNulls(JsonNode.Parse(serverWire))),
                CanonicalJson.Serialize(WithoutNulls(JsonNode.Parse(clientResult))),
                StringComparison.Ordinal
            );
        }
        catch (Exception ex)
            when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static JsonNode? WithoutNulls(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var kept = new JsonObject();
                foreach (var (name, value) in obj)
                    if (
                        value is not null
                        && value.GetValueKind() != System.Text.Json.JsonValueKind.Null
                    )
                        kept[name] = WithoutNulls(value.DeepClone());
                return kept;
            case JsonArray array:
                return new JsonArray(
                    array.Select(item => WithoutNulls(item?.DeepClone())).ToArray()
                );
            default:
                return node?.DeepClone();
        }
    }

    private AdvanceOutcome? Checked(Snapshot snapshot, string? clientResult) =>
        clientResult is null ? null : Checked(machine.Serialize(snapshot), clientResult);

    // The runtime differential: the client's twin computed its own result for this advance, and it must
    // equal the server's canonical wire byte for byte. A divergence means the two engines disagreed on a real
    // transition, so the advance is refused and the client reloads. It is checked before the write, so the
    // refusal is true: nothing was persisted.
    private AdvanceOutcome? Checked(string serverWire, string? clientResult)
    {
        if (
            clientResult is null
            || string.Equals(clientResult, serverWire, StringComparison.Ordinal)
            || SameSnapshot(serverWire, clientResult)
        )
            return null;
        StateMachineMeter.ClientDivergence.Add(
            1,
            new KeyValuePair<string, object?>("machine", _machineId)
        );
        return new AdvanceOutcome.Rejected(
            RefusalCode.ClientDivergence,
            "The client and server disagree on this transition; reload to continue."
        );
    }
}
