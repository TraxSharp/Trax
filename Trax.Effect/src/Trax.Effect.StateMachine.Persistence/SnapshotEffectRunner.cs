using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The machine-agnostic face of <see cref="SnapshotEffectRunner{TState,TTrigger}"/> (for the registry).</summary>
public interface ISnapshotEffectRunner
{
    /// <summary>
    /// Runs the machine's effect for the caller's draft at most once and commits its receipt by firing the
    /// effect's trigger. A draft already in the target state replays as <see cref="AdvanceOutcome.Advanced"/>
    /// without running anything. A draft in any state other than the effect's from-state, or a
    /// <paramref name="requestId"/> recorded for a different trigger, is <see cref="AdvanceOutcome.Rejected"/>
    /// before the effect runs; a claim still held by another caller is rejected as <c>effect-in-progress</c>. A claim
    /// whose effect already ran on content other than the draft's current content is rejected as
    /// <c>draft-changed</c>: its receipt is not recorded and the effect does not run again.
    /// </summary>
    /// <param name="userKey">The authenticated owner of the draft; a null, empty or whitespace key is refused.</param>
    /// <param name="id">The draft id.</param>
    /// <param name="requestId">The client's idempotency key for this send; a retry with the same id replays.</param>
    /// <param name="cancellationToken">
    /// Cancels the request up to the effect. It is not passed to the effect, which gets
    /// <see cref="CancellationToken.None"/>, and once the effect has returned, its receipt and the advance that
    /// records it are written whatever the token says, so a cancelled request never leaves an effect that ran
    /// looking as if it had not.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="userKey"/> is null, empty or whitespace.</exception>
    Task<AdvanceOutcome> Run(
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// The generic exactly-once orchestration for the ONE irreversible transition of a machine (place an
/// order, send a letter, charge a card). It closes the residual a bare concurrency token cannot: the
/// effect fires BEFORE the state write, so N truly-simultaneous requests would each deliver unless the
/// effect itself is claim-gated. This claims the intent (via <see cref="IdempotentEffect"/>) BEFORE
/// running the effect, then advances the machine authoritatively — so two concurrent runs deliver once
/// and a crash-retry replays the receipt.
///
/// <para>The flow: <c>Load -&gt; (already at target? replay) -&gt; (wrong state? refuse before the effect)
/// -&gt; RunOnce(effect) -&gt; Advance(trigger, {receipt}) with idempotency</c>. The effect implementation
/// and the intent key are supplied by the host; everything else is mechanism.</para>
///
/// <para>Infrastructure built by <see cref="Machine{TState,TTrigger}.CreateEffectRunner"/>; not intended to be
/// constructed directly. Consumers get an <see cref="ISnapshotEffectRunner"/> from
/// <see cref="ISnapshotMachineRegistry.EffectRunner"/>.</para>
/// </summary>
/// <typeparam name="TState">The machine's state enum.</typeparam>
/// <typeparam name="TTrigger">The machine's trigger enum.</typeparam>
internal sealed class SnapshotEffectRunner<TState, TTrigger> : ISnapshotEffectRunner
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly SnapshotDraftService<TState, TTrigger> _drafts;
    private readonly ISnapshotEffect _effect;
    private readonly IdempotentEffect _idempotent;
    private readonly Func<string, Guid, string> _effectKey;
    private readonly string _fromState;
    private readonly string _toState;
    private readonly string _trigger;
    private readonly string _receiptKey;
    private readonly TimeSpan? _lease;

    /// <param name="drafts">The draft service that loads the draft and commits the effect's result to it.</param>
    /// <param name="effect">The side effect to run once per intent; its receipt is recorded on the draft.</param>
    /// <param name="idempotent">The exactly-once primitive that leases and fences the effect key.</param>
    /// <param name="fromState">The only state the effect may run from (e.g. Review/Preview) — enforced before the effect.</param>
    /// <param name="trigger">The trigger that commits the result (e.g. Place/Send).</param>
    /// <param name="toState">The terminal state the trigger lands in (e.g. Placed/Sent) — a draft already there replays.</param>
    /// <param name="effectKey">Produces the intent key (server-stable; names the intent, not the content).</param>
    /// <param name="receiptKey">The context key the receipt is written under by the reducer.</param>
    /// <param name="lease">How long one attempt holds the effect's lease; null uses the <see cref="IdempotentEffect"/> default.</param>
    public SnapshotEffectRunner(
        SnapshotDraftService<TState, TTrigger> drafts,
        ISnapshotEffect effect,
        IdempotentEffect idempotent,
        TState fromState,
        TTrigger trigger,
        TState toState,
        Func<string, Guid, string> effectKey,
        string receiptKey = "receipt",
        TimeSpan? lease = null
    )
    {
        _drafts = drafts;
        _effect = effect;
        _idempotent = idempotent;
        _effectKey = effectKey;
        _fromState = fromState.ToString()!;
        _toState = toState.ToString()!;
        _trigger = trigger.ToString()!;
        _receiptKey = receiptKey;
        _lease = lease;
    }

    /// <inheritdoc/>
    public async Task<AdvanceOutcome> Run(
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        SnapshotOwner.Require(userKey);
        switch (await _drafts.Load(userKey, id, cancellationToken))
        {
            case LoadResult.NotFound:
                return new AdvanceOutcome.NotFound();

            case LoadResult.Invalid invalid:
                return new AdvanceOutcome.LoadError(invalid.Code, invalid.Message)
                {
                    Exception = invalid.Exception,
                };

            case LoadResult.Loaded loaded:
                // Already effected -> replay the stored result; never run the effect twice.
                if (loaded.Snapshot.State == _toState)
                    return new AdvanceOutcome.Advanced(loaded.Snapshot);

                // Only the required state may run the effect. Refuse BEFORE the effect, so a wrong-state
                // request can't trigger a real delivery.
                if (loaded.Snapshot.State != _fromState)
                    return new AdvanceOutcome.Rejected(
                        "no-transition",
                        $"Only a {_fromState} draft can run this effect."
                    );

                // The advance below would refuse a request id recorded for a different trigger. Refuse it here
                // instead, before the effect, so the refusal never follows a real delivery.
                if (
                    await _drafts.IsReusedRequest(
                        userKey,
                        id,
                        requestId,
                        _trigger,
                        loaded.Snapshot.State,
                        cancellationToken
                    )
                )
                    return new AdvanceOutcome.Rejected(
                        "request-id-reused",
                        "This request id was already used for a different action. Send a new id."
                    );

                // Exactly-once DELIVERY: claim the effect key BEFORE running. Two concurrent runs (or a
                // crash-retry) run the effect once and replay the receipt. The claim records the fingerprint of the
                // content the effect runs on, so its receipt is only ever replayed onto that content.
                var fingerprint = SnapshotFingerprint.Of(_drafts.Serialize(loaded.Snapshot));
                string receipt;
                switch (
                    await _idempotent.RunOnce(
                        _effectKey(userKey, id),
                        fingerprint,
                        // The request's token stops here: a request that goes away while the effect runs must
                        // not turn an effect that happened into a cancellation.
                        () => _effect.Run(loaded.Snapshot, CancellationToken.None),
                        _lease,
                        cancellationToken
                    )
                )
                {
                    case EffectOutcome.Ran ran:
                        receipt = ran.Receipt;
                        break;
                    case EffectOutcome.AlreadyRan already:
                        // The effect ran on content this draft no longer holds. Committing its receipt here would
                        // record it against content it was not produced for, and running the effect again would
                        // repeat it, so neither happens. A claim with no fingerprint predates the check and
                        // replays as it always did.
                        if (
                            already.ContentFingerprint is { } ranOn
                            && !string.Equals(ranOn, fingerprint, StringComparison.Ordinal)
                        )
                            return new AdvanceOutcome.Rejected(
                                "draft-changed",
                                "This draft's action already ran, on content that has changed since. Restore the "
                                    + "content it ran on to record its result."
                            );
                        receipt = already.Receipt;
                        break;
                    case EffectOutcome.InProgress:
                        return new AdvanceOutcome.Rejected(
                            "effect-in-progress",
                            "This effect is already running."
                        );
                    default:
                        return new AdvanceOutcome.Rejected(
                            RejectionReasons.InternalError,
                            "Unknown effect outcome."
                        );
                }

                // Fold the receipt into the terminal snapshot, on the draft exactly as the effect loaded it: if it
                // was written since (an edit, a reset, a concurrent run's commit) the CAS loses (Conflict) and the
                // receipt is not recorded on content the effect did not act on. The claim keeps it, so the next
                // send replays it onto that same content rather than running the effect again. The effect has
                // happened, so this write runs on a token the caller cannot cancel: a request that goes away now
                // must still leave the draft showing what was done. The same rule as a train's outcome, in
                // docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md.
                return await _drafts.AdvanceAfterEffect(
                    userKey,
                    id,
                    _trigger,
                    new JsonObject { [_receiptKey] = receipt },
                    requestId,
                    loaded.Token,
                    CancellationToken.None
                );

            default:
                return new AdvanceOutcome.LoadError("unknown", "Unknown load result.");
        }
    }
}
