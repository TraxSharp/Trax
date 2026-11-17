namespace Trax.StateMachine.Persistence;

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
