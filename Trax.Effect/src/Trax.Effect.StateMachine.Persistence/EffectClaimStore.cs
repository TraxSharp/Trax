using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The outcome of claiming an effect key: won (with the fence token to complete/release under), or lost.</summary>
public abstract record ClaimResult
{
    public sealed record Won(Guid OwnerToken) : ClaimResult;

    public sealed record Lost : ClaimResult;

    private ClaimResult() { }
}

/// <summary>
/// Durable claim ledger for exactly-once side effects. Machine-agnostic: the <c>effectKey</c> names the
/// INTENT. The unique key is the lock; a <b>lease + fence token</b> make it safe against a claimant that
/// wins and then dies without completing.
/// </summary>
public interface IEffectClaimStore
{
    /// <summary>
    /// Claim the key with a lease. Wins by inserting a fresh row, OR by reclaiming an in-flight row whose
    /// lease has expired. Returns the fence token on a win, or <see cref="ClaimResult.Lost"/> if the key
    /// is actively claimed or already completed.
    /// </summary>
    Task<ClaimResult> TryClaim(
        string effectKey,
        TimeSpan lease,
        CancellationToken cancellationToken = default
    );

    /// <summary>Record the effect's result — but only if this caller still holds the claim (CAS on the fence token).</summary>
    Task<bool> Complete(
        string effectKey,
        Guid ownerToken,
        string receipt,
        CancellationToken cancellationToken = default
    );

    /// <summary>The stored receipt, or <c>null</c> if the key is unclaimed or claimed-but-in-flight.</summary>
    Task<string?> GetReceipt(string effectKey, CancellationToken cancellationToken = default);

    /// <summary>Release a claim unconditionally (the reset / new-intent path). Idempotent.</summary>
    Task Release(string effectKey, CancellationToken cancellationToken = default);

    /// <summary>Release a claim only if this caller still owns it and it is in flight (the runner's fail path).</summary>
    Task<bool> ReleaseOwned(
        string effectKey,
        Guid ownerToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sweeper: delete in-flight claims whose lease expired before <paramref name="cutoff"/>. Returns the count.</summary>
    Task<int> ReclaimStale(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

/// <summary>The Postgres-backed <see cref="IEffectClaimStore"/>. The unique PK on <c>effect_key</c> is the lock.</summary>
public sealed class EfEffectClaimStore(SnapshotDbContext db) : IEffectClaimStore
{
    public async Task<ClaimResult> TryClaim(
        string effectKey,
        TimeSpan lease,
        CancellationToken cancellationToken = default
    )
    {
        var owner = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expires = now + lease;

        db.EffectClaims.Add(
            new EffectClaim
            {
                EffectKey = effectKey,
                OwnerToken = owner,
                LeaseExpiresAt = expires,
                Receipt = null,
                CreatedAt = now,
            }
        );

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new ClaimResult.Won(owner);
        }
        catch (DbUpdateException ex) when (EfSnapshotStore.IsUniqueViolation(ex))
        {
            // The key exists. Clear the failed Add so the context stays usable, then try to reclaim it —
            // but ONLY if it is an in-flight claim (no receipt) whose lease has expired.
            db.ChangeTracker.Clear();
            var rows = await db
                .EffectClaims.Where(x =>
                    x.EffectKey == effectKey && x.Receipt == null && x.LeaseExpiresAt < now
                )
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(x => x.OwnerToken, owner)
                            .SetProperty(x => x.LeaseExpiresAt, expires),
                    cancellationToken
                );
            return rows == 1 ? new ClaimResult.Won(owner) : new ClaimResult.Lost();
        }
    }

    public async Task<bool> Complete(
        string effectKey,
        Guid ownerToken,
        string receipt,
        CancellationToken cancellationToken = default
    )
    {
        var rows = await db
            .EffectClaims.Where(x =>
                x.EffectKey == effectKey && x.OwnerToken == ownerToken && x.Receipt == null
            )
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Receipt, receipt), cancellationToken);
        return rows == 1;
    }

    public async Task<string?> GetReceipt(
        string effectKey,
        CancellationToken cancellationToken = default
    ) =>
        (
            await db
                .EffectClaims.AsNoTracking()
                .FirstOrDefaultAsync(x => x.EffectKey == effectKey, cancellationToken)
        )?.Receipt;

    public Task Release(string effectKey, CancellationToken cancellationToken = default) =>
        db.EffectClaims.Where(x => x.EffectKey == effectKey).ExecuteDeleteAsync(cancellationToken);

    public async Task<bool> ReleaseOwned(
        string effectKey,
        Guid ownerToken,
        CancellationToken cancellationToken = default
    )
    {
        var rows = await db
            .EffectClaims.Where(x =>
                x.EffectKey == effectKey && x.OwnerToken == ownerToken && x.Receipt == null
            )
            .ExecuteDeleteAsync(cancellationToken);
        return rows == 1;
    }

    public Task<int> ReclaimStale(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default
    ) =>
        db
            .EffectClaims.Where(x => x.Receipt == null && x.LeaseExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}
