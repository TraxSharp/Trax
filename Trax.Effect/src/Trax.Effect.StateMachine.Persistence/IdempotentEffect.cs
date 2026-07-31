namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The result of <see cref="IdempotentEffect.RunOnce"/>: the effect ran now (Ran), its recorded result
/// was replayed (AlreadyRan — a concurrent/retried caller), or another caller holds the claim and is
/// still in flight (InProgress).
/// </summary>
public abstract record EffectOutcome
{
    /// <summary>This call ran the effect exactly once; <see cref="Receipt"/> is its result.</summary>
    public sealed record Ran(string Receipt) : EffectOutcome;

    /// <summary>The effect already ran; <see cref="Receipt"/> is the recorded result, handed back without re-running.</summary>
    public sealed record AlreadyRan(string Receipt) : EffectOutcome;

    /// <summary>Another caller holds the claim and is mid-flight (no receipt yet). This call did NOT run.</summary>
    public sealed record InProgress : EffectOutcome;

    private EffectOutcome() { }
}

/// <summary>
/// Runs an irreversible effect EXACTLY ONCE per <c>effectKey</c>. Claims the key (with a lease) BEFORE
/// running — the claim is the lock a bare optimistic-concurrency token can't be, because the effect
/// happens before the state write. A concurrent or retried call replays the stored receipt; a call that
/// finds the claim actively in flight returns <see cref="EffectOutcome.InProgress"/>.
///
/// <para><b>Liveness (lease + fence).</b> If the runner dies between claiming and completing, the claim's
/// lease expires and the next caller reclaims the key and re-runs — so a hard crash never wedges the key
/// forever. If the crashed runner then revives, its <c>Complete</c>/<c>ReleaseOwned</c> is fenced out by
/// the owner token (the reclaimer holds a new one), so it cannot corrupt the new claimant's result.</para>
/// </summary>
public sealed class IdempotentEffect(IEffectClaimStore claims)
{
    public async Task<EffectOutcome> RunOnce(
        string effectKey,
        Func<Task<string>> effect,
        TimeSpan? lease = null,
        CancellationToken cancellationToken = default
    )
    {
        switch (
            await claims.TryClaim(
                effectKey,
                lease ?? SnapshotLimits.DefaultEffectLease,
                cancellationToken
            )
        )
        {
            case ClaimResult.Won won:
                string receipt;
                try
                {
                    receipt = await effect();
                }
                catch
                {
                    // The effect failed before completing — release (fenced on our token) so a retry can
                    // re-run rather than being stuck behind an in-flight claim. Assumes throw = did not run.
                    await claims.ReleaseOwned(effectKey, won.OwnerToken, CancellationToken.None);
                    throw;
                }

                // Record the receipt against OUR claim. If this returns false our lease expired and the
                // claim was reclaimed mid-effect; the fence stops us corrupting the new owner's row. The
                // effect still ran exactly once here, so we hand back its receipt.
                await claims.Complete(effectKey, won.OwnerToken, receipt, cancellationToken);
                return new EffectOutcome.Ran(receipt);

            case ClaimResult.Lost:
                var existing = await claims.GetReceipt(effectKey, cancellationToken);
                return existing is null
                    ? new EffectOutcome.InProgress()
                    : new EffectOutcome.AlreadyRan(existing);

            default:
                return new EffectOutcome.InProgress();
        }
    }
}

/// <summary>
/// Releases abandoned in-flight claims (a claimant that won and then died without completing). Runs on a
/// schedule (wire it to a Trax.Scheduler manifest) as a backstop; on-demand reclaim in
/// <see cref="IEffectClaimStore.TryClaim"/> already frees a key on the next attempt.
/// </summary>
public sealed class EffectClaimSweeper(IEffectClaimStore claims)
{
    /// <summary>Releases in-flight claims whose lease expired before <paramref name="cutoff"/>. Returns the count.</summary>
    public Task<int> Sweep(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        claims.ReclaimStale(cutoff, cancellationToken);
}
