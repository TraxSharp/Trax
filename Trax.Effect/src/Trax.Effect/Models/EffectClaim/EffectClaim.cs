using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.EffectClaim;

/// <summary>
/// Base model for <c>trax.effect_claim</c>: one idempotency claim for an exactly-once side effect. It holds the
/// intent <see cref="EffectKey"/>, the effect's <see cref="Receipt"/> once it has run, and a <b>lease + fence
/// token</b> that make the claim safe against an abandoned runner. The primary key is the lock.
/// </summary>
/// <remarks>
/// EF Core mapping lives in <c>Trax.Effect.Data.Models.EffectClaim.PersistentEffectClaim</c> (in
/// Trax.Effect.Data). The table ships in the core migration set, and Trax.Effect.StateMachine.Persistence reaches
/// it through <c>IDataContext.EffectClaims</c> rather than through a context or SQL of its own.
/// </remarks>
public class EffectClaim
{
    /// <summary>The intent key (e.g. <c>checkout:charge:{userKey}:{draftId}</c>). Unique = the lock.</summary>
    [Column("effect_key")]
    public string EffectKey { get; set; } = null!;

    /// <summary>The effect's result, recorded once it has run. Null = claimed but the effect is in flight.</summary>
    [Column("receipt")]
    public string? Receipt { get; set; }

    /// <summary>
    /// A fingerprint of the content the effect runs on, recorded when the claim is taken (and again when an expired
    /// claim is taken over), so a receipt is only ever replayed onto that same content. Null on a claim recorded
    /// before the column existed, or by a caller that passed none; such a claim replays its receipt unchecked.
    /// </summary>
    [Column("content_fingerprint")]
    public string? ContentFingerprint { get; set; }

    /// <summary>
    /// The fence token of the current claimant. Completing or releasing the claim compares it, so a runner whose
    /// lease expired and was reclaimed cannot complete or delete the new claimant's row.
    /// </summary>
    [Column("owner_token")]
    public Guid OwnerToken { get; set; }

    /// <summary>
    /// When this in-flight claim's lease expires. A claim with a null receipt whose lease has passed is
    /// reclaimable by the next caller (and by the sweeper): the liveness guard against a runner that won the
    /// claim and then died without completing. On SQLite it is stored as fixed-width UTC text, so it compares in
    /// time order.
    /// </summary>
    [Column("lease_expires_at")]
    public DateTimeOffset LeaseExpiresAt { get; set; }

    /// <summary>
    /// When the row was first inserted (UTC). A reclaim after an expired lease keeps the original value, so this
    /// is the first attempt's time, not the current claimant's.
    /// </summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
