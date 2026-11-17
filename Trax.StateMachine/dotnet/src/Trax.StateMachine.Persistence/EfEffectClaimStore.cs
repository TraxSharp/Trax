using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using EffectClaim = Trax.Effect.Models.EffectClaim.EffectClaim;

namespace Trax.StateMachine.Persistence;

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
    public Task<ClaimResult> TryClaim(
        string effectKey,
        TimeSpan lease,
        CancellationToken cancellationToken = default
    ) => TryClaim(effectKey, lease, contentFingerprint: null, cancellationToken);

    /// <summary>
    /// Claims the key as <see cref="TryClaim(string, TimeSpan, CancellationToken)"/> does, writing
    /// <paramref name="contentFingerprint"/> to <c>content_fingerprint</c> on the row it inserts or takes over.
    /// </summary>
    /// <param name="effectKey">The intent key, the primary key of the row.</param>
    /// <param name="lease">How long the claim is held before another caller may reclaim it.</param>
    /// <param name="contentFingerprint">The fingerprint of the content the effect runs on; null records none.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    public async Task<ClaimResult> TryClaim(
        string effectKey,
        TimeSpan lease,
        string? contentFingerprint,
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
            ContentFingerprint = contentFingerprint,
            CreatedAt = now,
        };
        db.EffectClaims.Add(claim);

        try
        {
            await ((DbContext)db).SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
                            .SetProperty(x => x.LeaseExpiresAt, expires)
                            .SetProperty(x => x.ContentFingerprint, contentFingerprint),
                    cancellationToken
                )
                .ConfigureAwait(false);
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
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Receipt, receipt), cancellationToken)
            .ConfigureAwait(false);
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
                .ConfigureAwait(false)
        )?.Receipt;

    /// <inheritdoc/>
    public async Task<CompletedEffect?> GetCompleted(
        string effectKey,
        CancellationToken cancellationToken = default
    ) =>
        await db
            .EffectClaims.AsNoTracking()
            .Where(x => x.EffectKey == effectKey && x.Receipt != null)
            .Select(x => new CompletedEffect(x.Receipt!, x.ContentFingerprint))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Deletes the key's row whatever its state, completed or in flight, so the next
    /// <see cref="TryClaim(string, TimeSpan, CancellationToken)"/> starts over and the effect can run again. Used when a draft is deleted.
    /// </summary>
    /// <param name="effectKey">The intent key to forget.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    public Task Release(string effectKey, CancellationToken cancellationToken = default) =>
        db.EffectClaims.Where(x => x.EffectKey == effectKey).ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Deletes the key's row when the reset it follows settles it: an in-flight row whose lease has passed, or a
    /// completed row whose receipt <paramref name="receiptRecorded"/> accepts. Each delete is conditioned on the
    /// row as it was read, so a claim that completes in between is kept.
    /// </summary>
    /// <param name="effectKey">The intent key of the draft being reset.</param>
    /// <param name="receiptRecorded">Whether the draft being reset holds a given receipt.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    public async Task ReleaseForReset(
        string effectKey,
        Func<string, bool> receiptRecorded,
        CancellationToken cancellationToken = default
    )
    {
        var claim = await db
            .EffectClaims.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EffectKey == effectKey, cancellationToken)
            .ConfigureAwait(false);
        if (claim is null)
            return;

        if (claim.Receipt is null)
        {
            var now = DateTimeOffset.UtcNow;
            await db
                .EffectClaims.Where(x =>
                    x.EffectKey == effectKey && x.Receipt == null && x.LeaseExpiresAt < now
                )
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!receiptRecorded(claim.Receipt))
            return;
        var receipt = claim.Receipt;
        await db
            .EffectClaims.Where(x => x.EffectKey == effectKey && x.Receipt == receipt)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Whether the key's claim, completed or not, was taken longer ago than the lease it was taken with.</summary>
    /// <param name="effectKey">The intent key.</param>
    internal Task<bool> LeasePassed(string effectKey)
    {
        var now = DateTimeOffset.UtcNow;
        return db
            .EffectClaims.AsNoTracking()
            .AnyAsync(x => x.EffectKey == effectKey && x.LeaseExpiresAt < now);
    }

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
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
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
