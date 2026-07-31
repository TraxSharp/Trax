using System.Text;
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The total result of <see cref="SnapshotDraftService{TState,TTrigger}.Load"/>.</summary>
public abstract record LoadResult
{
    public sealed record Loaded(Snapshot Snapshot) : LoadResult;

    public sealed record NotFound : LoadResult;

    public sealed record Invalid(string Code, string Message) : LoadResult;

    private LoadResult() { }
}

/// <summary>The total result of <see cref="SnapshotDraftService{TState,TTrigger}.Autosave"/>.</summary>
public abstract record AutosaveResult
{
    public sealed record Saved(Snapshot Snapshot) : AutosaveResult;

    public sealed record Rejected(string Code, string Message) : AutosaveResult;

    /// <summary>The draft changed elsewhere between read and write — reload and retry.</summary>
    public sealed record Conflict : AutosaveResult;

    private AutosaveResult() { }
}

/// <summary>The total result of <see cref="SnapshotDraftService{TState,TTrigger}.Advance"/>.</summary>
public abstract record AdvanceOutcome
{
    public sealed record Advanced(Snapshot Snapshot) : AdvanceOutcome;

    public sealed record Rejected(string Reason, string? Detail) : AdvanceOutcome;

    public sealed record NotFound : AdvanceOutcome;

    public sealed record LoadError(string Code, string Message) : AdvanceOutcome;

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
    Task<LoadResult> Load(string userKey, Guid id, CancellationToken cancellationToken = default);

    Task<AutosaveResult> Autosave(
        string userKey,
        Guid id,
        string snapshotJson,
        CancellationToken cancellationToken = default
    );

    Task<AdvanceOutcome> Advance(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input = null,
        string? requestId = null,
        CancellationToken cancellationToken = default
    );

    string Serialize(Snapshot snapshot);
}

/// <summary>
/// The FE-drives / BE-validates operations over a persisted, user-scoped snapshot, built on the total
/// <see cref="SnapshotMachine{TState,TTrigger}"/> engine and an <see cref="ISnapshotStore"/>. Every
/// method is total for expected outcomes — including concurrency conflicts, which come back as a typed
/// <c>Conflict</c> rather than a thrown <c>DbUpdateException</c>.
/// </summary>
public sealed class SnapshotDraftService<TState, TTrigger>(
    SnapshotMachine<TState, TTrigger> machine,
    ISnapshotStore store,
    IReadOnlyCollection<TState>? committedStates = null,
    IEffectClaimStore? effectClaims = null,
    Func<string, Guid, IEnumerable<string>>? effectKeysOnReset = null
) : ISnapshotDraftService
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    // States a SOFT autosave must not move a draft OUT of (except a reset to the initial state) — e.g. a
    // Paid order. This is what stops a stale/racing autosave from resurrecting a completed draft and
    // letting an irreversible action happen a second time. Empty => the fast last-writer-wins path.
    private readonly HashSet<string> _committedStates = BuildStateSet(committedStates);
    private readonly string _initialState = machine.Definition.InitialState.ToString()!;

    private static HashSet<string> BuildStateSet(IReadOnlyCollection<TState>? states)
    {
        var set = new HashSet<string>();
        if (states is not null)
            foreach (var s in states)
                set.Add(s.ToString()!);
        return set;
    }

    // On returning to the initial state (a reset / "start over"), release any effect claims for this
    // instance so the NEXT logical effect can claim a clean key — otherwise the next effect would replay
    // the previous one's receipt and never run. Idempotent; a no-op when no effects are wired.
    private async Task ReleaseEffectClaims(
        string userKey,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        if (effectClaims is null || effectKeysOnReset is null)
            return;
        foreach (var key in effectKeysOnReset(userKey, id))
            await effectClaims.Release(key, cancellationToken);
    }

    private async Task<AutosaveResult> Persisted(
        bool ok,
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        if (!ok)
            return new AutosaveResult.Conflict();
        if (snapshot.State == _initialState)
            await ReleaseEffectClaims(userKey, id, cancellationToken);
        return new AutosaveResult.Saved(snapshot);
    }

    public string Serialize(Snapshot snapshot) => machine.Serialize(snapshot);

    /// <summary>Read the caller's draft, validating the stored data on the way out.</summary>
    public async Task<LoadResult> Load(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var stored = await store.Get(userKey, id, cancellationToken);
        if (stored is null)
            return new LoadResult.NotFound();

        return machine.Rehydrate(stored.Json) switch
        {
            RehydrationResult.Ok ok => new LoadResult.Loaded(ok.Snapshot),
            RehydrationResult.Error error => new LoadResult.Invalid(error.Code, error.Message),
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
                // Fast path: nothing to protect => blind last-writer-wins autosave.
                if (_committedStates.Count == 0)
                {
                    var upserted = await store.Upsert(userKey, id, ok.Snapshot, cancellationToken);
                    return await Persisted(upserted, userKey, id, ok.Snapshot, cancellationToken);
                }

                // Guarded path: a soft save must not resurrect a COMMITTED draft (e.g. Paid -> Review) and
                // let an irreversible action happen again. The only committed -> X a soft save may make is a
                // reset to the initial state or a same-state update.
                var stored = await store.Get(userKey, id, cancellationToken);
                if (stored is null)
                {
                    var inserted = await store.Upsert(userKey, id, ok.Snapshot, cancellationToken);
                    return await Persisted(inserted, userKey, id, ok.Snapshot, cancellationToken);
                }
                if (
                    machine.Rehydrate(stored.Json) is RehydrationResult.Ok current
                    && _committedStates.Contains(current.Snapshot.State)
                    && ok.Snapshot.State != current.Snapshot.State
                    && ok.Snapshot.State != _initialState
                )
                    return new AutosaveResult.Rejected(
                        "draft-committed",
                        "This draft was already completed and can't be overwritten by an edit."
                    );

                // Atomic overwrite guarded by the token we just read: a commit that lands between this read
                // and this write makes the soft save LOSE (Conflict) instead of resurrecting the draft.
                var wrote = await store.Update(
                    userKey,
                    id,
                    ok.Snapshot,
                    stored.Token,
                    stored.LastRequestId,
                    cancellationToken
                );
                return await Persisted(wrote, userKey, id, ok.Snapshot, cancellationToken);

            case RehydrationResult.Error error:
                return new AutosaveResult.Rejected(error.Code, error.Message);
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
    /// <paramref name="requestId"/> to make retries idempotent: a repeat of the same request returns the
    /// current snapshot instead of firing the trigger again.
    /// </summary>
    public async Task<AdvanceOutcome> Advance(
        string userKey,
        Guid id,
        string trigger,
        JsonNode? input = null,
        string? requestId = null,
        CancellationToken cancellationToken = default
    )
    {
        var stored = await store.Get(userKey, id, cancellationToken);
        if (stored is null)
            return new AdvanceOutcome.NotFound();

        // Idempotent replay: this exact request already applied — return the current snapshot, don't re-fire.
        if (requestId is not null && requestId == stored.LastRequestId)
            return RehydrateToOutcome(stored.Json);

        switch (machine.Rehydrate(stored.Json))
        {
            case RehydrationResult.Error error:
                return new AdvanceOutcome.LoadError(error.Code, error.Message);

            case RehydrationResult.Ok ok:
                switch (machine.Advance(ok.Snapshot, trigger, input))
                {
                    case AdvanceResult.Rejected rejected:
                        return new AdvanceOutcome.Rejected(rejected.Reason, rejected.Detail);
                    case AdvanceResult.Transitioned transitioned:
                        var updated = await store.Update(
                            userKey,
                            id,
                            transitioned.Snapshot,
                            stored.Token,
                            requestId,
                            cancellationToken
                        );
                        if (!updated)
                            return new AdvanceOutcome.Conflict();
                        if (transitioned.Snapshot.State == _initialState)
                            await ReleaseEffectClaims(userKey, id, cancellationToken);
                        return new AdvanceOutcome.Advanced(transitioned.Snapshot);
                    default:
                        return new AdvanceOutcome.Rejected(RejectionReasons.InternalError, null);
                }

            default:
                return new AdvanceOutcome.LoadError(
                    RehydrationErrorCodes.Malformed,
                    "Unknown rehydration result."
                );
        }
    }

    private AdvanceOutcome RehydrateToOutcome(string json) =>
        machine.Rehydrate(json) switch
        {
            RehydrationResult.Ok ok => new AdvanceOutcome.Advanced(ok.Snapshot),
            RehydrationResult.Error error => new AdvanceOutcome.LoadError(
                error.Code,
                error.Message
            ),
            _ => new AdvanceOutcome.LoadError(
                RehydrationErrorCodes.Malformed,
                "Unknown rehydration result."
            ),
        };
}
