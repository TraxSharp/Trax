using System.Text;
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The total result of <see cref="SnapshotDraftService{TState,TTrigger}.Load"/>.</summary>
public abstract record LoadResult
{
    /// <summary>The draft exists and rehydrated cleanly.</summary>
    /// <param name="Snapshot">The validated snapshot, at the machine's current definition version.</param>
    public sealed record Loaded(Snapshot Snapshot) : LoadResult
    {
        /// <summary>
        /// The concurrency token the draft was read with. The effect runner commits its receipt only while the draft
        /// still carries it, so the receipt is recorded on the content the effect ran on.
        /// </summary>
        internal Guid Token { get; init; }
    }

    /// <summary>
    /// No draft exists for this user and id, or it had been idle longer than the configured draft TTL and
    /// was deleted by this read. Treat it as "start fresh".
    /// </summary>
    public sealed record NotFound : LoadResult;

    /// <summary>A stored row exists but failed rehydration; the row is left untouched.</summary>
    /// <param name="Code">A rehydration error code, one of <see cref="RehydrationErrorCodes"/>.</param>
    /// <param name="Message">A human-readable explanation of what failed validation.</param>
    public sealed record Invalid(string Code, string Message) : LoadResult
    {
        /// <summary>The exception behind an unexpected failure, kept for the server's log and never in <see cref="Message"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    private LoadResult() { }
}

/// <summary>The total result of <see cref="SnapshotDraftService{TState,TTrigger}.Autosave"/>.</summary>
public abstract record AutosaveResult
{
    /// <summary>The client snapshot validated and was persisted.</summary>
    /// <param name="Snapshot">The snapshot as stored, after rehydration (and any version migration).</param>
    public sealed record Saved(Snapshot Snapshot) : AutosaveResult;

    /// <summary>The snapshot was refused and nothing was written.</summary>
    /// <param name="Code">
    /// <c>too-large</c> (over <see cref="SnapshotLimits.MaxSnapshotBytes"/>), <c>draft-committed</c> (the stored
    /// draft is in a committed state or an effect's target and the save would change it in any way other than a
    /// reset to the initial state that the machine declares from that state), <c>state-reserved</c> (the save would create a draft in, or move one into, a committed state or
    /// the target of an effect-bound transition, which only the effect runner reaches), <c>draft-unreadable</c>
    /// (the stored draft fails rehydration, so only a reset to the initial state may overwrite it), or a
    /// <see cref="RehydrationErrorCodes"/> value when the payload failed validation.
    /// </param>
    /// <param name="Message">A human-readable explanation of the refusal.</param>
    public sealed record Rejected(string Code, string Message) : AutosaveResult
    {
        /// <summary>The exception behind an unexpected failure, kept for the server's log and never in <see cref="Message"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    /// <summary>The draft changed elsewhere between read and write — reload and retry.</summary>
    public sealed record Conflict : AutosaveResult;

    private AutosaveResult() { }
}

/// <summary>The total result of <see cref="SnapshotDraftService{TState,TTrigger}.Advance(string, Guid, string, JsonNode, string, CancellationToken)"/>.</summary>
public abstract record AdvanceOutcome
{
    /// <summary>
    /// The trigger fired and the result was persisted, or the request was a replay of the one the draft
    /// last recorded and nothing was fired again.
    /// </summary>
    /// <param name="Snapshot">The draft's snapshot after the advance (or the current one, on a replay).</param>
    public sealed record Advanced(Snapshot Snapshot) : AdvanceOutcome;

    /// <summary>The advance was refused and the stored draft is unchanged.</summary>
    /// <param name="Reason">
    /// A machine rejection reason (see <see cref="RejectionReasons"/>), or one of the service's own:
    /// <c>effect-bound</c> (the trigger runs the machine's irreversible effect from this state, so only
    /// <see cref="ISnapshotEffectRunner.Run"/> may fire it), <c>request-id-reused</c>, <c>too-large</c>,
    /// <c>draft-changed</c> (from <see cref="ISnapshotEffectRunner.Run"/> only: the effect already ran on content
    /// the draft no longer holds, so its receipt is not recorded and the effect does not run again),
    /// <c>client-divergence</c>, or <see cref="RehydrationErrorCodes.Malformed"/> when the resulting context
    /// could not be stored.
    /// </param>
    /// <param name="Detail">Extra context for the reason, such as the failing guard; null when there is none.</param>
    public sealed record Rejected(string Reason, string? Detail) : AdvanceOutcome
    {
        /// <summary>The exception behind an <c>internal-error</c>, kept for the server's log and never in <see cref="Detail"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    /// <summary>No draft exists for this user and id, so there is nothing to advance.</summary>
    public sealed record NotFound : AdvanceOutcome;

    /// <summary>The stored draft failed rehydration, so it could not be advanced; it is left untouched.</summary>
    /// <param name="Code">A rehydration error code, one of <see cref="RehydrationErrorCodes"/>.</param>
    /// <param name="Message">A human-readable explanation of what failed validation.</param>
    public sealed record LoadError(string Code, string Message) : AdvanceOutcome
    {
        /// <summary>The exception behind an unexpected failure, kept for the server's log and never in <see cref="Message"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    /// <summary>Another writer advanced this draft first — the client's view is stale.</summary>
    public sealed record Conflict : AdvanceOutcome;

    private AdvanceOutcome() { }
}

/// <summary>
/// The machine-agnostic face of <see cref="SnapshotDraftService{TState,TTrigger}"/>. Its methods take and
/// return only strings/JSON and the non-generic result unions, so a registry can hold one of these per
/// machine keyed by name and a single generic mutation can serve every machine.
/// </summary>
public interface ISnapshotDraftService
{
    /// <summary>
    /// Reads the caller's draft and rehydrates it, validating the stored JSON on the way out. A draft idle
    /// past the configured TTL is deleted, with its effect claims, and reported as <see cref="LoadResult.NotFound"/>.
    /// </summary>
    /// <param name="userKey">The owner of the draft; drafts are always scoped to one user.</param>
    /// <param name="id">The draft's id.</param>
    /// <param name="cancellationToken">Cancels the store read.</param>
    Task<LoadResult> Load(string userKey, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The soft path: validates a client-provided snapshot and persists it as-is. Invalid or oversized
    /// data is never stored. The soft path never creates a committed state: a save that would create a draft
    /// in, or move one into, a committed state or an effect-bound transition's target is rejected as
    /// <c>state-reserved</c>. Nor does it rewrite a draft already in one: any change to such a draft is rejected as
    /// <c>draft-committed</c> except a reset to the initial state that the machine itself declares from that
    /// state, and a save identical to the stored draft is answered as saved without writing. A stored draft that
    /// fails rehydration may only be reset to the initial state (anything else is <c>draft-unreadable</c>), a new
    /// draft is only ever created, never written over one created meanwhile, and a concurrent write yields
    /// <c>Conflict</c>. A machine with no committed states and no effect is last writer wins. Saving a snapshot in
    /// the initial state releases the draft's settled effect claims: one still in flight within its lease, or a
    /// completed one whose receipt the replaced draft never recorded, is kept, so the effect is not run again.
    /// </summary>
    /// <param name="userKey">The owner of the draft.</param>
    /// <param name="id">The draft's id; the draft is created if it does not exist.</param>
    /// <param name="snapshotJson">The client's serialized snapshot.</param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    Task<AutosaveResult> Autosave(
        string userKey,
        Guid id,
        string snapshotJson,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The authoritative path: reads the stored snapshot, fires <paramref name="trigger"/> on it, and
    /// persists the result under an optimistic-concurrency check, never trusting a client-computed state.
    /// A lost race comes back as <c>Conflict</c>, not an exception. A trigger bound to the machine's
    /// irreversible effect from the stored state is refused as <c>effect-bound</c>: only the effect runner
    /// (the send path) fires it, after the effect has run.
    /// </summary>
    /// <param name="userKey">The owner of the draft.</param>
    /// <param name="id">The draft's id; it must already exist.</param>
    /// <param name="trigger">The trigger name, matched against the machine's trigger enum.</param>
    /// <param name="input">The trigger's input, validated against its declared schema; null when it takes none.</param>
    /// <param name="requestId">
    /// An optional idempotency key. A repeat of the request the draft last recorded (same id and trigger)
    /// returns the current snapshot without firing again, unless the draft has since moved back to the
    /// state that request fired from, in which case it fires as a new request. The same id with a
    /// different trigger is refused as <c>request-id-reused</c>. Null never replays.
    /// </param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    Task<AdvanceOutcome> Advance(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input = null,
        string? requestId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// <see cref="Advance(string, Guid, string, JsonNode?, string?, CancellationToken)"/> that first
    /// compares the result with <paramref name="clientResult"/>, the canonical snapshot the client's twin
    /// computed, and persists nothing when they differ: the advance is refused as
    /// <c>client-divergence</c> and the stored draft is unchanged. A null <paramref name="clientResult"/>
    /// is a plain advance. A service that does not override this cannot check before it writes, so it
    /// refuses any client result rather than persisting an advance it would then report as refused.
    /// </summary>
    Task<AdvanceOutcome> Advance(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        string? clientResult,
        CancellationToken cancellationToken = default
    ) =>
        clientResult is null
            ? Advance(userKey, id, trigger, input, requestId, cancellationToken)
            : throw new NotSupportedException(
                $"{GetType().Name} cannot compare a client result before it persists an advance."
            );

    /// <summary>
    /// Serializes a snapshot to the machine's canonical wire JSON: the exact string a client twin must
    /// produce for a <c>clientResult</c> comparison to match.
    /// </summary>
    /// <param name="snapshot">The snapshot to serialize.</param>
    string Serialize(Snapshot snapshot);
}

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
    TimeSpan? draftTtl = null,
    IReadOnlyList<EffectBinding<TState, TTrigger>>? effects = null
) : ISnapshotDraftService
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

    // The (from, trigger) edges that run the machine's irreversible effect. Only the effect runner fires them,
    // after the effect ran, so the receipt the reducer records is one the effect produced.
    private readonly HashSet<(string From, string Trigger)> _effectEdges =
        effects?.Select(e => (e.From.ToString()!, e.Trigger.ToString()!)).ToHashSet() ?? [];

    // The states the soft path may not create or move a draft into: every committed state and every state an
    // effect-bound transition lands in. A draft reaches them through the effect runner only.
    private readonly HashSet<string> _reservedStates = BuildStateSet(committedStates)
        .Concat(effects?.Select(e => e.To.ToString()!) ?? [])
        .ToHashSet();

    private static HashSet<string> BuildStateSet(IReadOnlyCollection<TState>? states)
    {
        var set = new HashSet<string>();
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
        var recorded = RecordedStrings(replacedJson);
        foreach (var key in effectKeysOnReset(userKey, id))
            await effectClaims.ReleaseForReset(key, recorded, CancellationToken.None);
    }

    // Every string the replaced draft's context holds: a receipt the reducer recorded is one of them, wherever the
    // reducer put it. A draft that cannot be parsed records nothing, so its completed claims are kept.
    private static Func<string, bool> RecordedStrings(string? replacedJson)
    {
        if (replacedJson is null)
            return _ => true;
        var strings = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            Collect(JsonNode.Parse(replacedJson)?["context"], strings);
        }
        catch (System.Text.Json.JsonException)
        {
            // Unreadable: nothing is recorded.
        }
        return strings.Contains;

        static void Collect(JsonNode? node, HashSet<string> into)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (_, child) in o)
                        Collect(child, into);
                    break;
                case JsonArray a:
                    foreach (var child in a)
                        Collect(child, into);
                    break;
                case JsonValue v when v.TryGetValue<string>(out var text):
                    into.Add(text);
                    break;
            }
        }
    }

    // Every draft deletion goes through here, so no deleted draft leaves its effect claims behind. The draft is
    // gone as a new intent's starting point, so its claims go whatever their state. The claims are released
    // before the row is deleted, and neither write can be cancelled: a request that goes away between them would
    // otherwise leave claims with no draft, which nothing would ever read or release again.
    private async Task Delete(string userKey, Guid id)
    {
        if (effectClaims is not null && effectKeysOnReset is not null)
            foreach (var key in effectKeysOnReset(userKey, id))
                await effectClaims.Release(key, CancellationToken.None);
        await store.Delete(userKey, _machineId, id, CancellationToken.None);
    }

    private async Task<AutosaveResult> Persisted(
        bool ok,
        string userKey,
        Guid id,
        Snapshot snapshot,
        string? replacedJson
    )
    {
        if (!ok)
            return new AutosaveResult.Conflict();
        if (snapshot.State == _initialState)
            await ReleaseSettledClaims(userKey, id, replacedJson);
        return new AutosaveResult.Saved(snapshot);
    }

    // Whether the machine itself resets a draft in this state: a transition from it to the initial state.
    private bool MachineResets(string state) =>
        machine.Definition.Transitions.Any(t =>
            t.From.ToString() == state && t.To.ToString() == _initialState
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
            "state-reserved",
            "This step is completed by its action, not by saving the draft. Send it instead."
        );

    /// <inheritdoc/>
    public string Serialize(Snapshot snapshot) => machine.Serialize(snapshot);

    /// <summary>Read the caller's draft, validating the stored data on the way out.</summary>
    public async Task<LoadResult> Load(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var stored = await store.Get(userKey, _machineId, id, cancellationToken);
        if (stored is null)
            return new LoadResult.NotFound();

        // Lazy on-read expiry: a draft idle past the TTL is treated as abandoned. Delete the row (so an
        // expired COMMITTED draft is cleared uniformly, not just ignored) and report NotFound, which every
        // caller already handles as "no draft, start fresh". A null TTL never expires. This is a resume-time
        // decision only; Advance/Autosave never yank an active session's draft. Deleting a draft also releases
        // its effect claims, exactly as a reset does: the next draft under the same id (a well-known id is
        // allowed) is a new intent, and a kept claim would answer its effect with the old receipt.
        if (draftTtl is { } ttl && stored.UpdatedAt < DateTimeOffset.UtcNow - ttl)
        {
            await Delete(userKey, id);
            return new LoadResult.NotFound();
        }

        return machine.Rehydrate(stored.Json) switch
        {
            RehydrationResult.Ok ok => new LoadResult.Loaded(ok.Snapshot) { Token = stored.Token },
            RehydrationResult.Error error => new LoadResult.Invalid(error.Code, error.Message)
            {
                Exception = error.Exception,
            },
            _ => new LoadResult.Invalid(
                RehydrationErrorCodes.Malformed,
                "Unknown rehydration result."
            ),
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
        // Bound the payload before any parsing or DB work (DoS guard).
        if (Encoding.UTF8.GetByteCount(snapshotJson) > SnapshotLimits.MaxSnapshotBytes)
            return new AutosaveResult.Rejected(
                "too-large",
                $"Snapshot exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        switch (machine.Rehydrate(snapshotJson))
        {
            case RehydrationResult.Ok ok:
                // Fast path: nothing to protect => blind last-writer-wins autosave. A store that does not key by
                // machine is read first, so a draft of another machine under this id is never overwritten.
                if (_reservedStates.Count == 0)
                {
                    var upserted =
                        KeysByMachine(store)
                        || await store.Get(userKey, _machineId, id, cancellationToken) is not null
                            ? await store.Upsert(userKey, id, ok.Snapshot, cancellationToken)
                            : await store.Insert(userKey, id, ok.Snapshot, cancellationToken);
                    return await Persisted(upserted, userKey, id, ok.Snapshot, replacedJson: null);
                }

                // Guarded path. A soft save never creates a committed state: it may not create a draft in, or
                // move one into, a committed state or an effect's target, because only the effect runner puts a
                // draft there. Nor may it rewrite a draft already there: the receipt such a draft holds is the one
                // the effect produced for exactly that content, so the only write allowed is a reset the machine
                // itself declares, and a save identical to what is stored is answered without writing.
                var stored = await store.Get(userKey, _machineId, id, cancellationToken);
                var entering = _reservedStates.Contains(ok.Snapshot.State);
                if (stored is null)
                {
                    if (entering)
                        return Reserved();
                    // Insert, never Upsert: a draft created since the read above (and sent, perhaps) must make
                    // this save lose rather than be overwritten by it.
                    var inserted = await store.Insert(userKey, id, ok.Snapshot, cancellationToken);
                    return await Persisted(inserted, userKey, id, ok.Snapshot, replacedJson: null);
                }

                // A stored draft that cannot be read may be committed, so nothing but a reset overwrites it.
                if (machine.Rehydrate(stored.Json) is not RehydrationResult.Ok current)
                {
                    if (ok.Snapshot.State != _initialState)
                        return new AutosaveResult.Rejected(
                            "draft-unreadable",
                            "The saved draft can't be read, so it can only be started over."
                        );
                }
                else if (_reservedStates.Contains(current.Snapshot.State))
                {
                    if (
                        string.Equals(
                            machine.Serialize(ok.Snapshot),
                            machine.Serialize(current.Snapshot),
                            StringComparison.Ordinal
                        )
                    )
                        return new AutosaveResult.Saved(current.Snapshot);
                    if (
                        ok.Snapshot.State != _initialState
                        || !MachineResets(current.Snapshot.State)
                    )
                        return new AutosaveResult.Rejected(
                            "draft-committed",
                            "This draft was already completed and can't be overwritten by an edit."
                        );
                }
                else if (entering)
                    return Reserved();

                // Atomic overwrite guarded by the token we just read: a commit that lands between this read
                // and this write makes the soft save LOSE (Conflict) instead of resurrecting the draft.
                var wrote = await store.UpdateWithRequest(
                    userKey,
                    id,
                    ok.Snapshot,
                    stored.Token,
                    stored.LastRequest,
                    cancellationToken
                );
                return await Persisted(wrote, userKey, id, ok.Snapshot, stored.Json);

            case RehydrationResult.Error error:
                return new AutosaveResult.Rejected(error.Code, error.Message)
                {
                    Exception = error.Exception,
                };
            default:
                return new AutosaveResult.Rejected(
                    RehydrationErrorCodes.Malformed,
                    "Unknown rehydration result."
                );
        }
    }

    /// <summary>
    /// Authoritative path: read the STORED snapshot, re-drive it by one trigger, and persist the result
    /// with an optimistic-concurrency check — never trusting a client-computed state. Pass a stable
    /// <paramref name="requestId"/> to make retries idempotent: a repeat of the same request (the same id
    /// and trigger) returns the current snapshot instead of firing the trigger again, and an id reused for
    /// a different trigger is refused as <c>request-id-reused</c>. A result that could not be stored, or is
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
            effectRan: false,
            expectedToken: null,
            cancellationToken
        );

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
            effectRan: true,
            expectedToken,
            cancellationToken
        );

    private async Task<AdvanceOutcome> AdvanceCore(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        string? clientResult,
        bool effectRan,
        Guid? expectedToken,
        CancellationToken cancellationToken
    )
    {
        var stored = await store.Get(userKey, _machineId, id, cancellationToken);
        if (stored is null)
            return new AdvanceOutcome.NotFound();

        // The draft changed after the effect's load: whatever is stored now is not what the effect acted on.
        if (expectedToken is { } expected && stored.Token != expected)
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
                    RehydrationErrorCodes.Malformed,
                    "Unknown rehydration result."
                );
        }

        // An effect-bound edge records the effect's receipt. Fired from here, the receipt would be whatever the
        // caller put in the input and the effect would never have run, so only the effect runner fires it.
        if (!effectRan && _effectEdges.Contains((current.State, trigger)))
            return new AdvanceOutcome.Rejected(
                "effect-bound",
                "This action runs an irreversible effect. Send it instead of advancing."
            );

        switch (Retry(stored.LastRequest, requestId, trigger, current.State))
        {
            case RetryKind.Replay:
                return Checked(current, clientResult) ?? new AdvanceOutcome.Advanced(current);
            case RetryKind.Reused:
                return new AdvanceOutcome.Rejected(
                    "request-id-reused",
                    "This request id was already used for a different action. Send a new id."
                );
        }

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
            case AdvanceResult.Transitioned transitioned:
                next = transitioned.Snapshot;
                break;
            default:
                return new AdvanceOutcome.Rejected(RejectionReasons.InternalError, null);
        }

        if (StorableJson.Problem(next.Context) is { } unstorable)
            return new AdvanceOutcome.Rejected(RehydrationErrorCodes.Malformed, unstorable);

        var wire = machine.Serialize(next);
        if (Encoding.UTF8.GetByteCount(wire) > SnapshotLimits.MaxSnapshotBytes)
            return new AdvanceOutcome.Rejected(
                "too-large",
                $"The advanced snapshot exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        if (Checked(wire, clientResult) is { } divergence)
            return divergence;

        var updated = await store.UpdateWithRequest(
            userKey,
            id,
            next,
            stored.Token,
            requestId is null ? null : new AppliedRequest(requestId, trigger, current.State),
            cancellationToken
        );
        if (!updated)
            return new AdvanceOutcome.Conflict();
        if (next.State == _initialState)
            await ReleaseSettledClaims(userKey, id, stored.Json);
        return new AdvanceOutcome.Advanced(next);
    }

    /// <summary>
    /// Whether <paramref name="requestId"/> is one the draft recorded for a different trigger, so an advance
    /// on <paramref name="trigger"/> with it would be refused as <c>request-id-reused</c>. The effect runner
    /// asks before it runs an irreversible effect, so a refused id never pays for a delivery it cannot record.
    /// </summary>
    internal async Task<bool> IsReusedRequest(
        string userKey,
        Guid id,
        string requestId,
        string trigger,
        string currentState,
        CancellationToken cancellationToken
    ) =>
        await store.Get(userKey, _machineId, id, cancellationToken) is { } stored
        && Retry(stored.LastRequest, requestId, trigger, currentState) == RetryKind.Reused;

    private enum RetryKind
    {
        /// <summary>Not a retry: fire the trigger.</summary>
        Fire,

        /// <summary>A retry of the recorded request: answer with the current snapshot.</summary>
        Replay,

        /// <summary>The recorded id with a different trigger (or none recorded): refuse.</summary>
        Reused,
    }

    // Whether this request repeats the one the draft last recorded. The id alone is not enough: a client
    // (or Send's default key) can reuse an id for a different trigger, and replaying then answers one action
    // with the outcome of another. A matching id and trigger replay, unless the draft is back in the state
    // the request fired from on an edge that leaves it: then the request's outcome is gone (the draft was
    // reset or moved back since) and the same request is a new one.
    private RetryKind Retry(
        AppliedRequest? last,
        string? requestId,
        string trigger,
        string currentState
    )
    {
        if (requestId is null || last is null || requestId != last.RequestId)
            return RetryKind.Fire;
        if (last.Trigger != trigger || last.FromState is null)
            return RetryKind.Reused;
        if (currentState == last.FromState && !IsSelfLoop(last.FromState, trigger))
            return RetryKind.Fire;
        return RetryKind.Replay;
    }

    private bool IsSelfLoop(string state, string trigger) =>
        machine.Definition.Transitions.Any(t =>
            t.From.ToString() == state && t.Trigger.ToString() == trigger && t.To.Equals(t.From)
        );

    private AdvanceOutcome? Checked(Snapshot snapshot, string? clientResult) =>
        clientResult is null ? null : Checked(machine.Serialize(snapshot), clientResult);

    // The runtime differential: the client's twin computed its own result for this advance, and it must
    // equal the server's canonical wire byte for byte. A divergence means the two engines disagreed on a real
    // transition, so the advance is refused and the client reloads. It is checked before the write, so the
    // refusal is true: nothing was persisted.
    private static AdvanceOutcome? Checked(string serverWire, string? clientResult) =>
        clientResult is not null
        && !string.Equals(clientResult, serverWire, StringComparison.Ordinal)
            ? new AdvanceOutcome.Rejected(
                "client-divergence",
                "The client and server disagree on this transition; reload to continue."
            )
            : null;
}
