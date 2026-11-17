using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The machine-agnostic face of <see cref="SnapshotDraftService{TState,TTrigger}"/>. Its methods take and
/// return only strings/JSON and the non-generic result unions, so a registry can hold one of these per
/// machine keyed by name and a single generic mutation can serve every machine.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#isnapshotdraftservice">SDK reference</seealso>
public interface ISnapshotDraftService
{
    /// <summary>
    /// Reads the caller's draft and rehydrates it, validating the stored JSON on the way out. A draft idle
    /// past the configured TTL is deleted, with its effect claims, and reported as <see cref="LoadResult.NotFound"/>.
    /// </summary>
    /// <param name="userKey">
    /// The owner of the draft; drafts are always scoped to one user. A null, empty or whitespace key names no
    /// owner and is refused, on every method of this service.
    /// </param>
    /// <param name="id">The draft's id.</param>
    /// <param name="cancellationToken">Cancels the store read.</param>
    /// <exception cref="ArgumentException"><paramref name="userKey"/> is null, empty or whitespace.</exception>
    Task<LoadResult> Load(string userKey, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="Load(string, Guid, CancellationToken)"/> on behalf of <paramref name="actor"/>: a
    /// <see cref="Actor.Client"/> reads only its own drafts, and only when it meets the machine's declared
    /// <c>Get</c> rule beyond that owner-only floor. A caller the rule does not admit, a client with no user key, and
    /// any actor that is not a client are answered <see cref="LoadResult.NotFound"/>, exactly as an unknown id is, so
    /// the answer reveals nothing. A service that does not override this answers <see cref="LoadResult.NotFound"/>
    /// to every actor.
    /// </summary>
    /// <param name="actor">Who reads it.</param>
    /// <param name="id">The draft's id.</param>
    /// <param name="cancellationToken">Cancels the store read and the rule's policy checks.</param>
    Task<LoadResult> Load(Actor actor, Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<LoadResult>(new LoadResult.NotFound());

    /// <summary>
    /// The soft path: validates a client-provided snapshot and persists it as-is. Invalid or oversized
    /// data is never stored. The soft path never creates a committed state: a save that would create a draft
    /// in, or move one into, a committed state or an effect-bound transition's target is rejected as
    /// <c>STATE_RESERVED</c>. Nor does it rewrite a draft already in one: any change to such a draft is rejected as
    /// <c>DRAFT_COMMITTED</c> except a reset to the initial state that the machine itself declares from that
    /// state, and a save identical to the stored draft is answered as saved without writing. A stored draft that
    /// fails rehydration may only be reset to the initial state (anything else is <c>DRAFT_UNREADABLE</c>), a new
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
    /// irreversible effect from the stored state is refused as <c>EFFECT_BOUND</c>: only the effect runner
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
    /// different trigger is refused as <c>REQUEST_ID_REUSED</c>. Null never replays.
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
    /// <c>CLIENT_DIVERGENCE</c> and the stored draft is unchanged. A null <paramref name="clientResult"/>
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

    /// <summary>
    /// <see cref="Advance(string, Guid, string, JsonNode?, string?, CancellationToken)"/> on behalf of
    /// <paramref name="actor"/>: a <see cref="Actor.Client"/> acts on its own drafts and must meet the event's
    /// declared rule, checked per event against the roles and policies it holds now. One that does not is refused
    /// as <c>FORBIDDEN</c> before a request id is replayed, the input is validated or anything is written, after
    /// only an unknown trigger (<c>NO_TRANSITION</c>) and an effect- or outcome-bound one. A client with no user
    /// key is refused as <c>FORBIDDEN</c>. Host code acting on a user's draft is a client too, holding the roles
    /// it is given; host code drives a system-owned instance through <see cref="IMachineInstances"/>. On a machine that declares no rule at all, an authenticated client may fire
    /// every event, as before rules; once a machine declares any rule, an event without one is refused.
    /// A service that does not override this refuses every actor.
    /// </summary>
    Task<AdvanceOutcome> Advance(
        Actor actor,
        Guid id,
        string trigger,
        JsonNode? input = null,
        string? requestId = null,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<AdvanceOutcome>(
            new AdvanceOutcome.Rejected(
                RefusalCode.Forbidden,
                $"{GetType().Name} does not check event rules."
            )
        );

    /// <summary>
    /// <see cref="Advance(Actor, Guid, string, JsonNode?, string?, CancellationToken)"/> that first compares the
    /// result with <paramref name="clientResult"/>, as
    /// <see cref="Advance(string, Guid, string, JsonNode?, string?, string?, CancellationToken)"/> does. A
    /// service that does not override this refuses every actor.
    /// </summary>
    Task<AdvanceOutcome> Advance(
        Actor actor,
        Guid id,
        string trigger,
        JsonNode? input,
        string? requestId,
        string? clientResult,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<AdvanceOutcome>(
            new AdvanceOutcome.Rejected(
                RefusalCode.Forbidden,
                $"{GetType().Name} does not check event rules."
            )
        );

    /// <summary>
    /// Runs the machine's effect for the draft on behalf of <paramref name="actor"/>, as
    /// <see cref="ISnapshotEffectRunner.Run"/> does, after checking a <see cref="Actor.Client"/> against the
    /// effect-bound event's declared rule, though a send names no trigger: a caller the rule does not admit is
    /// refused as <c>FORBIDDEN</c> before the draft is read or the effect claimed. A machine with no effect answers
    /// <c>NO_EFFECT</c>. A service that does not override this refuses every actor.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The service was built by <see cref="IMachine.CreateService"/> rather than taken from
    /// <see cref="ISnapshotMachineRegistry.Service"/>, so it has no effect runner.
    /// </exception>
    Task<AdvanceOutcome> Send(
        Actor actor,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<AdvanceOutcome>(
            new AdvanceOutcome.Rejected(
                RefusalCode.Forbidden,
                $"{GetType().Name} does not check event rules."
            )
        );

    /// <summary>
    /// Starts a new instance for a client from the machine's declared start input: the input is validated against
    /// the start's record (never the context type), the start's rule is checked, the declared reduction is applied to
    /// the initial context, and the server mints the id; the row is inserted at revision 1, with the run its initial
    /// state invokes, if any, in the same transaction. A retry with the same <paramref name="requestId"/> and input
    /// returns the same instance with <c>Created</c> false; the same request id with a different input is refused as
    /// <c>REQUEST_ID_REUSED</c>. Refusals write nothing and come back as <see cref="StartOutcome.Rejected"/> (see
    /// <see cref="RefusalCode"/>). A service that does not implement it throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="actor">Who starts it: a <see cref="Actor.Client"/> naming a user; anything else is refused.</param>
    /// <param name="input">The start input; null when the machine's start takes none.</param>
    /// <param name="requestId">
    /// The client's idempotency key for this start: 1 to <see cref="RequestIdRing.MaxRequestIdLength"/> characters,
    /// not blank. Unique per owner, user and machine.
    /// </param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    Task<StartOutcome> Start(
        Actor actor,
        JsonObject? input,
        string requestId,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException($"{GetType().Name} cannot start an instance.");

    /// <summary>
    /// Deletes the client's draft <paramref name="id"/>, cancelling a run its state invoked first and releasing
    /// its settled effect claims with it. Idempotent: a draft already gone, or another user's id, answers
    /// <see cref="DiscardOutcome.Discarded"/>, exactly as a missing one does. A system-owned instance is refused. An
    /// instance in a state only the server puts it in, or one an effect is running on or still recording its result
    /// to, is refused as <c>DISCARD_NOT_ALLOWED</c> and kept; so is one that moved into such a state while the
    /// discard ran, because the delete is conditional on the row as it was checked. A service that does not implement
    /// it throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="actor">Who discards it: a <see cref="Actor.Client"/> naming a user; anything else is refused.</param>
    /// <param name="id">The instance id.</param>
    /// <param name="requestId">The client's idempotency key for this discard.</param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    Task<DiscardOutcome> Discard(
        Actor actor,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException($"{GetType().Name} cannot discard an instance.");
}
