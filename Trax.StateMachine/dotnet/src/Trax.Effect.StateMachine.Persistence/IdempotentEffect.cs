namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The result of <see cref="IdempotentEffect.RunOnce(string, Func{Task{string}}, TimeSpan?, CancellationToken)"/>: the effect ran now (Ran), its recorded result
/// was replayed (AlreadyRan — a concurrent/retried caller), or another caller holds the claim and is
/// still in flight (InProgress).
/// </summary>
public abstract record EffectOutcome
{
    /// <summary>This call ran the effect exactly once; <see cref="Receipt"/> is its result.</summary>
    public sealed record Ran(string Receipt) : EffectOutcome;

    /// <summary>The effect already ran; <see cref="Receipt"/> is the recorded result, handed back without re-running.</summary>
    public sealed record AlreadyRan(string Receipt) : EffectOutcome
    {
        /// <summary>
        /// The fingerprint of the content the effect ran on, as its claim recorded it, or null when the claim
        /// recorded none. A caller that passed a fingerprint compares the two before using <see cref="Receipt"/>.
        /// </summary>
        public string? ContentFingerprint { get; init; }
    }

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
///
/// <para><b>A receipt is required.</b> An effect that returns a null or empty receipt has failed as far as
/// the ledger is concerned: its claim is released and <see cref="RunOnce(string, Func{Task{string}}, TimeSpan?, CancellationToken)"/> throws
/// <see cref="InvalidOperationException"/>, exactly as if the effect had thrown.</para>
/// </summary>
public sealed class IdempotentEffect(IEffectClaimStore claims)
{
    /// <summary>
    /// Claims <paramref name="effectKey"/>, runs <paramref name="effect"/> if the claim was won, and records its
    /// receipt. A lost claim returns <see cref="EffectOutcome.AlreadyRan"/> with the stored receipt, or
    /// <see cref="EffectOutcome.InProgress"/> while another caller is still in flight; the effect does not run.
    /// If the effect throws, the claim is released (fenced on this call's token) and the exception rethrown, so a
    /// retry runs it again: a throw is assumed to mean the effect did not happen. An
    /// <see cref="OperationCanceledException"/> is the exception to that: the outcome is unknown, so the claim stays
    /// in flight until its lease passes and callers until then get <see cref="EffectOutcome.InProgress"/>. If this call's lease expired
    /// mid-effect and another caller reclaimed the key, the receipt is not recorded but is still returned as
    /// <see cref="EffectOutcome.Ran"/>.
    /// </summary>
    /// <param name="effectKey">The intent key; one key runs its effect at most once until it is released.</param>
    /// <param name="effect">The side effect. It must return a non-empty receipt once it has run.</param>
    /// <param name="lease">
    /// How long the claim is held before another caller may reclaim it; null uses
    /// <see cref="SnapshotLimits.DefaultEffectLease"/> (5 minutes). Set it longer than the effect can take.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the claim, before the effect runs. It is not passed to <paramref name="effect"/>, and once the effect
    /// has returned its receipt is recorded whatever the token says: a cancellation then would leave the claim in
    /// flight until its lease passed, and the next caller would run the effect again.
    /// </param>
    /// <exception cref="InvalidOperationException">The effect returned a null or empty receipt; its claim was released.</exception>
    public Task<EffectOutcome> RunOnce(
        string effectKey,
        Func<Task<string>> effect,
        TimeSpan? lease = null,
        CancellationToken cancellationToken = default
    ) => RunOnce(effectKey, contentFingerprint: null, effect, lease, cancellationToken);

    /// <summary>
    /// Runs the effect as <see cref="RunOnce(string, Func{Task{string}}, TimeSpan?, CancellationToken)"/> does, and
    /// records <paramref name="contentFingerprint"/> on the claim it wins. A lost claim's
    /// <see cref="EffectOutcome.AlreadyRan"/> carries the fingerprint its claim recorded, so the caller can tell a
    /// receipt for this content from one for content that has changed since.
    /// </summary>
    /// <param name="effectKey">The intent key; one key runs its effect at most once until it is released.</param>
    /// <param name="contentFingerprint">
    /// The fingerprint of the content the effect runs on (see <see cref="SnapshotFingerprint"/>); null records none.
    /// </param>
    /// <param name="effect">The side effect. It must return a non-empty receipt once it has run.</param>
    /// <param name="lease">How long the claim is held; null uses <see cref="SnapshotLimits.DefaultEffectLease"/>.</param>
    /// <param name="cancellationToken">Cancels the claim, before the effect runs; never passed to the effect.</param>
    /// <exception cref="InvalidOperationException">The effect returned a null or empty receipt; its claim was released.</exception>
    public async Task<EffectOutcome> RunOnce(
        string effectKey,
        string? contentFingerprint,
        Func<Task<string>> effect,
        TimeSpan? lease = null,
        CancellationToken cancellationToken = default
    )
    {
        switch (
            await claims.TryClaim(
                effectKey,
                lease ?? SnapshotLimits.DefaultEffectLease,
                contentFingerprint,
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
                catch (OperationCanceledException)
                {
                    // A cancellation says nothing about whether the effect happened: a charge can land and its
                    // response time out. Keep the claim in flight, so no one runs the effect again until the lease
                    // passes, rather than releasing it for an immediate retry.
                    throw;
                }
                catch
                {
                    // The effect failed before completing — release (fenced on our token) so a retry can
                    // re-run rather than being stuck behind an in-flight claim. Assumes throw = did not run.
                    await claims.ReleaseOwned(effectKey, won.OwnerToken, CancellationToken.None);
                    throw;
                }

                // The ledger reads a claim with no receipt as still in flight, and reclaims it once the lease
                // passes, so recording a null or empty receipt would let the effect run again. Treat it as a
                // failed effect instead: release the claim and say so.
                if (string.IsNullOrEmpty(receipt))
                {
                    await claims.ReleaseOwned(effectKey, won.OwnerToken, CancellationToken.None);
                    throw new InvalidOperationException(
                        $"The effect for '{effectKey}' returned no receipt, so it was treated as failed and its "
                            + "claim was released. An effect must return a non-empty receipt once it has run."
                    );
                }

                // Record the receipt against OUR claim. If this returns false our lease expired and the
                // claim was reclaimed mid-effect; the fence stops us corrupting the new owner's row. The
                // effect still ran exactly once here, so we hand back its receipt. The effect has happened, so
                // the caller's token no longer applies: a cancelled write here would leave the claim in flight
                // and let the next caller run the effect again. The same rule as a train's outcome, in
                // docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md.
                await claims.Complete(effectKey, won.OwnerToken, receipt, CancellationToken.None);
                return new EffectOutcome.Ran(receipt);

            case ClaimResult.Lost:
                var existing = await claims.GetCompleted(effectKey, cancellationToken);
                return existing is null
                    ? new EffectOutcome.InProgress()
                    : new EffectOutcome.AlreadyRan(existing.Receipt)
                    {
                        ContentFingerprint = existing.ContentFingerprint,
                    };

            default:
                return new EffectOutcome.InProgress();
        }
    }
}

/// <summary>
/// Releases abandoned in-flight claims (a claimant that won and then died without completing). Runs on a
/// schedule (wire it to a Trax.Scheduler manifest) as a backstop; on-demand reclaim in
/// <see cref="IEffectClaimStore.TryClaim(string, TimeSpan, CancellationToken)"/> already frees a key on the next attempt.
/// </summary>
public sealed class EffectClaimSweeper(IEffectClaimStore claims)
{
    /// <summary>Releases in-flight claims whose lease expired before <paramref name="cutoff"/>. Returns the count.</summary>
    public Task<int> Sweep(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        claims.ReclaimStale(cutoff, cancellationToken);
}
