using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using EffectClaim = Trax.Effect.Models.EffectClaim.EffectClaim;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The outcome of claiming an effect key: won (with the fence token to complete/release under), or lost.</summary>
public abstract record ClaimResult
{
    /// <summary>
    /// This caller holds the claim: it inserted the key, or reclaimed an in-flight claim whose lease had expired.
    /// Run the effect, then pass <c>OwnerToken</c> to <see cref="IEffectClaimStore.Complete"/> or
    /// <see cref="IEffectClaimStore.ReleaseOwned"/>.
    /// </summary>
    /// <param name="OwnerToken">
    /// The fence token minted for this claim. Completing or releasing succeeds only while the row still carries
    /// it, so a claimant whose lease was taken over cannot touch the new owner's row.
    /// </param>
    public sealed record Won(Guid OwnerToken) : ClaimResult;

    /// <summary>
    /// Another caller holds the key: either its claim is still inside its lease, or the effect already completed.
    /// Read <see cref="IEffectClaimStore.GetReceipt"/> to tell the two apart.
    /// </summary>
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

/// <summary>
/// The <see cref="IEffectClaimStore"/> over <see cref="IDataContext.EffectClaims"/>. The primary key on
/// <c>effect_key</c> is the lock.
/// </summary>
/// <param name="db">The data context the table is reached through.</param>
/// <param name="dialect">
/// Recognises a unique violation on the configured provider, which is how a claim on a key someone else holds is
/// told apart from a real failure. Without one, such a claim throws instead of reporting
/// <see cref="ClaimResult.Lost"/>.
/// </param>
public sealed class EfEffectClaimStore(IDataContext db, ISqlDialect? dialect = null)
    : IEffectClaimStore
{
    /// <summary>
    /// Inserts a new <c>effect_claim</c> row with a fresh owner token and <c>lease_expires_at = now + lease</c>.
    /// If the key already exists, it takes the row over only when the receipt is null and the lease has passed,
    /// rotating the owner token; otherwise the result is <see cref="ClaimResult.Lost"/>. A completed claim is
    /// never reclaimed. The duplicate key is recognised through the <see cref="ISqlDialect"/> the store was given.
    /// </summary>
    /// <param name="effectKey">The intent key, the primary key of the row.</param>
    /// <param name="lease">How long the claim is held before another caller may reclaim it.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    public async Task<ClaimResult> TryClaim(
        string effectKey,
        TimeSpan lease,
        CancellationToken cancellationToken = default
    )
    {
        var owner = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expires = now + lease;

        var claim = new EffectClaim
        {
            EffectKey = effectKey,
            OwnerToken = owner,
            LeaseExpiresAt = expires,
            Receipt = null,
            CreatedAt = now,
        };
        db.EffectClaims.Add(claim);

        try
        {
            await ((DbContext)db).SaveChangesAsync(cancellationToken);
            return new ClaimResult.Won(owner);
        }
        catch (DbUpdateException ex) when (dialect?.IsUniqueViolation(ex) == true)
        {
            // The key exists. Try to reclaim it — but ONLY if it is an in-flight claim (no receipt) whose lease
            // has expired.
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
        finally
        {
            // Stop tracking the claim whether or not it was written: the context may be shared with the rest of
            // the request, and a failed insert left tracked would be retried by the next save on it.
            ((DbContext)db)
                .Entry(claim)
                .State = EntityState.Detached;
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task<string?> GetReceipt(
        string effectKey,
        CancellationToken cancellationToken = default
    ) =>
        (
            await db
                .EffectClaims.AsNoTracking()
                .FirstOrDefaultAsync(x => x.EffectKey == effectKey, cancellationToken)
        )?.Receipt;

    /// <summary>
    /// Deletes the key's row whatever its state, completed or in flight, so the next
    /// <see cref="TryClaim"/> starts over and the effect can run again. Used when a draft is reset.
    /// </summary>
    /// <param name="effectKey">The intent key to forget.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    public Task Release(string effectKey, CancellationToken cancellationToken = default) =>
        db.EffectClaims.Where(x => x.EffectKey == effectKey).ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public Task<int> ReclaimStale(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default
    ) =>
        db
            .EffectClaims.Where(x => x.Receipt == null && x.LeaseExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}
