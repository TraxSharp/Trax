using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// One persisted draft, scoped to a user: the four snapshot fields as columns (with <c>context</c> a
/// real Postgres <c>jsonb</c> column), plus an app-managed optimistic-concurrency token.
///
/// <para><b>Identity is composite: <c>(user_key, id)</c>.</b> The draft <c>id</c> is chosen by the
/// client (a machine may use one well-known id for a user's "current" instance), so it is unique only
/// per user. The primary key must include <c>user_key</c> — a bare PK on <c>id</c> would let two users'
/// drafts collide on insert (one squats the id; every other user's create fails the PK and their
/// autosave silently never persists). The id is also a client-minted Guid, so it is globally unique in
/// practice too; the composite key is the belt-and-suspenders guarantee.</para>
/// </summary>
[Table("snapshot_draft")]
public class SnapshotRecord
{
    /// <summary>The draft id — client-minted (a Guid), unique within a user (see the composite key).</summary>
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>The owning user's key. A draft is only visible to (and mutable by) its owner.</summary>
    [Column("user_key")]
    public string UserKey { get; set; } = default!;

    [Column("machine")]
    public string Machine { get; set; } = default!;

    [Column("version")]
    public int Version { get; set; }

    [Column("state")]
    public string State { get; set; } = default!;

    /// <summary>The per-state context, stored as a genuine <c>jsonb</c> column (queryable server-side).</summary>
    [Column("context", TypeName = "jsonb")]
    public string Context { get; set; } = "{}";

    /// <summary>
    /// App-managed optimistic-concurrency token (a fresh Guid on every write). Marked
    /// <c>IsConcurrencyToken</c> so a stale tracked write throws <c>DbUpdateConcurrencyException</c>;
    /// the atomic <c>Update</c> path guards on it in a WHERE clause instead. Provider-agnostic (works
    /// under <c>EnsureCreated</c>), unlike Postgres <c>xmin</c>.
    /// </summary>
    [Column("concurrency_token")]
    public Guid ConcurrencyToken { get; set; }

    /// <summary>The idempotency key of the last applied advance, if any (a retried advance replays).</summary>
    [Column("last_request_id")]
    public string? LastRequestId { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SnapshotRecord>();
        // The client-chosen id is unique only per user; (user_key, id) also serves the user-scoped reads
        // via its leading column, so no separate user_key index is needed.
        entity.HasKey(x => new { x.UserKey, x.Id });
        entity.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
    }
}

/// <summary>
/// One idempotency claim for an exactly-once side effect: the intent <see cref="EffectKey"/>, the
/// effect's <see cref="Receipt"/> once it has run, and a <b>lease + fence token</b> that make the claim
/// safe against an abandoned runner. The unique key is the lock. See <see cref="IdempotentEffect"/>.
/// </summary>
[Table("effect_claim")]
public class EffectClaim
{
    /// <summary>The intent key (e.g. <c>checkout:charge:{userKey}:{draftId}</c>). Unique = the lock.</summary>
    [Column("effect_key")]
    public string EffectKey { get; set; } = default!;

    /// <summary>The effect's result, recorded once it has run. Null = claimed but the effect is in flight.</summary>
    [Column("receipt")]
    public string? Receipt { get; set; }

    /// <summary>
    /// The fence token of the current claimant. <c>Complete</c>/<c>ReleaseOwned</c> CAS on it, so a runner
    /// whose lease expired and was reclaimed cannot complete or delete the new claimant's row.
    /// </summary>
    [Column("owner_token")]
    public Guid OwnerToken { get; set; }

    /// <summary>
    /// When this in-flight claim's lease expires. A claim with a null receipt whose lease has passed is
    /// reclaimable by the next caller (and by the sweeper) — this is the liveness guard against a runner
    /// that won the claim and then died without completing.
    /// </summary>
    [Column("lease_expires_at")]
    public DateTimeOffset LeaseExpiresAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public static void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EffectClaim>().HasKey(x => x.EffectKey);
}

/// <summary>
/// A ready-made DbContext for the snapshot-draft and effect-claim tables in the <c>trax</c> schema. A
/// host may use this directly, or add the two entities to its own context via the entities'
/// <c>OnModelCreating</c> helpers. Tests build the tables via <c>EnsureCreated</c> against a throwaway
/// database; a production host applies the equivalent migration.
/// </summary>
public sealed class SnapshotDbContext(DbContextOptions<SnapshotDbContext> options)
    : DbContext(options)
{
    public DbSet<SnapshotRecord> SnapshotDrafts => Set<SnapshotRecord>();
    public DbSet<EffectClaim> EffectClaims => Set<EffectClaim>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("trax");
        SnapshotRecord.OnModelCreating(modelBuilder);
        EffectClaim.OnModelCreating(modelBuilder);
    }
}
