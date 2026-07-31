using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

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
/// </summary>
/// <summary>The machine-agnostic face of <see cref="SnapshotEffectRunner{TState,TTrigger}"/> (for the registry).</summary>
public interface ISnapshotEffectRunner
{
    Task<AdvanceOutcome> Run(
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    );
}

public sealed class SnapshotEffectRunner<TState, TTrigger> : ISnapshotEffectRunner
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly SnapshotDraftService<TState, TTrigger> _drafts;
    private readonly IEffect _effect;
    private readonly IdempotentEffect _idempotent;
    private readonly Func<string, Guid, string> _effectKey;
    private readonly string _fromState;
    private readonly string _toState;
    private readonly string _trigger;
    private readonly string _receiptKey;
    private readonly TimeSpan? _lease;

    /// <param name="fromState">The only state the effect may run from (e.g. Review/Preview) — enforced before the effect.</param>
    /// <param name="trigger">The trigger that commits the result (e.g. Place/Send).</param>
    /// <param name="toState">The terminal state the trigger lands in (e.g. Placed/Sent) — a draft already there replays.</param>
    /// <param name="effectKey">Produces the intent key (server-stable; names the intent, not the content).</param>
    /// <param name="receiptKey">The context key the receipt is written under by the reducer.</param>
    public SnapshotEffectRunner(
        SnapshotDraftService<TState, TTrigger> drafts,
        IEffect effect,
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

    public async Task<AdvanceOutcome> Run(
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        switch (await _drafts.Load(userKey, id, cancellationToken))
        {
            case LoadResult.NotFound:
                return new AdvanceOutcome.NotFound();

            case LoadResult.Invalid invalid:
                return new AdvanceOutcome.LoadError(invalid.Code, invalid.Message);

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

                // Exactly-once DELIVERY: claim the effect key BEFORE running. Two concurrent runs (or a
                // crash-retry) run the effect once and replay the receipt.
                string receipt;
                switch (
                    await _idempotent.RunOnce(
                        _effectKey(userKey, id),
                        () => _effect.Run(loaded.Snapshot, cancellationToken),
                        _lease,
                        cancellationToken
                    )
                )
                {
                    case EffectOutcome.Ran ran:
                        receipt = ran.Receipt;
                        break;
                    case EffectOutcome.AlreadyRan already:
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

                // Fold the receipt into the terminal snapshot. If a concurrent run already committed this
                // CAS loses (Conflict) — harmless: the effect ran once and the winner recorded it.
                return await _drafts.Advance(
                    userKey,
                    id,
                    _trigger,
                    new JsonObject { [_receiptKey] = receipt },
                    requestId,
                    cancellationToken
                );

            default:
                return new AdvanceOutcome.LoadError("unknown", "Unknown load result.");
        }
    }
}
