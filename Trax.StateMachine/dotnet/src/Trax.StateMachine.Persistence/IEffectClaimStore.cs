using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using EffectClaim = Trax.Effect.Models.EffectClaim.EffectClaim;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// Durable claim ledger for exactly-once side effects. Machine-agnostic: the <c>effectKey</c> names the
/// INTENT. The unique key is the lock; a <b>lease + fence token</b> make it safe against a claimant that
/// wins and then dies without completing.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#ieffectclaimstore">SDK reference</seealso>
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

    /// <summary>
    /// Claim the key as <see cref="TryClaim(string, TimeSpan, CancellationToken)"/> does, and record
    /// <paramref name="contentFingerprint"/>, the fingerprint of the content the effect is about to run on, on the
    /// claim it wins. <see cref="GetCompleted"/> hands it back with the receipt, so a caller can refuse to replay the
    /// receipt onto different content. A store that does not override this claims without recording it, and its
    /// completed claims replay unchecked.
    /// </summary>
    /// <param name="effectKey">The intent key.</param>
    /// <param name="lease">How long the claim is held before another caller may reclaim it.</param>
    /// <param name="contentFingerprint">The fingerprint to record with the claim; null records none.</param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    Task<ClaimResult> TryClaim(
        string effectKey,
        TimeSpan lease,
        string? contentFingerprint,
        CancellationToken cancellationToken = default
    ) => TryClaim(effectKey, lease, cancellationToken);

    /// <summary>Record the effect's result — but only if this caller still holds the claim (CAS on the fence token).</summary>
    Task<bool> Complete(
        string effectKey,
        Guid ownerToken,
        string receipt,
        CancellationToken cancellationToken = default
    );

    /// <summary>The stored receipt, or <see langword="null"/> if the key is unclaimed or claimed-but-in-flight.</summary>
    Task<string?> GetReceipt(string effectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The completed claim for the key, its receipt with the content fingerprint it was taken with, in one read; or
    /// <see langword="null"/> if the key is unclaimed or claimed-but-in-flight. A store that does not override this reads
    /// <see cref="GetReceipt"/> and reports no fingerprint.
    /// </summary>
    /// <param name="effectKey">The intent key.</param>
    /// <param name="cancellationToken">Cancels the store read.</param>
    async Task<CompletedEffect?> GetCompleted(
        string effectKey,
        CancellationToken cancellationToken = default
    ) =>
        await GetReceipt(effectKey, cancellationToken).ConfigureAwait(false) is { } receipt
            ? new CompletedEffect(receipt, ContentFingerprint: null)
            : null;

    /// <summary>
    /// Release a claim unconditionally, completed or in flight (the path for a deleted draft, whose next draft is a
    /// new intent). A reset releases through <see cref="ReleaseForReset"/> instead. Idempotent.
    /// </summary>
    Task Release(string effectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release a claim when the draft it belongs to is reset, but only once its outcome is settled on that draft:
    /// a claim in flight within its lease is kept (the effect may still be running), and a completed claim is
    /// released only when <paramref name="receiptRecorded"/> says the draft recorded its receipt. A kept completed
    /// claim is replayed by the next run instead of the effect running a second time. A store that does not
    /// override this reads the receipt and releases through <see cref="Release"/> only a completed claim whose
    /// receipt was recorded; it keeps every claim in flight, whatever its lease.
    /// </summary>
    /// <param name="effectKey">The intent key of the draft being reset.</param>
    /// <param name="receiptRecorded">Whether the draft being reset holds a given receipt.</param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    async Task ReleaseForReset(
        string effectKey,
        Func<string, bool> receiptRecorded,
        CancellationToken cancellationToken = default
    )
    {
        if (
            await GetReceipt(effectKey, cancellationToken).ConfigureAwait(false) is { } receipt
            && receiptRecorded(receipt)
        )
            await Release(effectKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Release a claim only if this caller still owns it and it is in flight (the runner's fail path).</summary>
    Task<bool> ReleaseOwned(
        string effectKey,
        Guid ownerToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>Sweeper: delete in-flight claims whose lease expired before <paramref name="cutoff"/>. Returns the count.</summary>
    Task<int> ReclaimStale(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
