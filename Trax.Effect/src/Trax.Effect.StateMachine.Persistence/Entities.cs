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

    /// <summary>
    /// The id of the machine this draft belongs to (its <see cref="IMachine.Name"/>), copied from the snapshot on
    /// every write. Not null.
    /// </summary>
    [Column("machine")]
    public string Machine { get; set; } = default!;

    /// <summary>
    /// The machine definition version the snapshot was written under, copied from the snapshot on every write.
    /// On load an older version is migrated forward through the machine's migrations; a newer one, or one with a
    /// gap in the chain, is refused as <c>version-mismatch</c>.
    /// </summary>
    [Column("version")]
    public int Version { get; set; }

    /// <summary>The current state's name (the <c>TState</c> enum member as text). Not null.</summary>
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

    /// <summary>
    /// The trigger the last applied advance fired. A request id replays only for the same trigger, so an id
    /// reused for a different trigger is refused rather than answered with an unrelated snapshot.
    /// </summary>
    [Column("last_request_trigger")]
    public string? LastRequestTrigger { get; set; }

    /// <summary>
    /// The state the last applied advance fired from. A draft back in that state (reset, or moved back) no
    /// longer shows the request's outcome, so the same request id and trigger fire again rather than replay.
    /// </summary>
    [Column("last_request_from_state")]
    public string? LastRequestFromState { get; set; }

    /// <summary>
    /// When the draft was last written, set by the store to the UTC clock on every insert and update. With a
    /// <see cref="StateMachineOptions.DraftTtl"/> set, a draft whose value is older than the TTL is deleted on its
    /// next load.
    /// </summary>
    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Maps this entity on <paramref name="modelBuilder"/>: the composite key <c>(user_key, id)</c> and
    /// <see cref="ConcurrencyToken"/> as a concurrency token. <see cref="SnapshotDbContext"/> calls it; a host
    /// that maps the table on its own context calls it from that context's <c>OnModelCreating</c>.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
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

    /// <summary>
    /// When the row was first inserted (UTC). A reclaim after an expired lease keeps the original value, so
    /// this is the first attempt's time, not the current claimant's.
    /// </summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Maps this entity on <paramref name="modelBuilder"/> with <see cref="EffectKey"/> as the primary key.
    /// <see cref="SnapshotDbContext"/> calls it; a host that maps the table on its own context calls it from
    /// that context's <c>OnModelCreating</c>.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    public static void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EffectClaim>().HasKey(x => x.EffectKey);
}

/// <summary>
/// A ready-made DbContext for the snapshot-draft and effect-claim tables in the <c>trax</c> schema. A
/// host may use this directly, or add the two entities to its own context via the entities'
/// <c>OnModelCreating</c> helpers. The tables ship as migrations that apply automatically when the data
/// provider is registered: Postgres via <c>040_state_machine_snapshots.sql</c> and
/// <c>048_snapshot_draft_request_scope.sql</c>, SQLite via <c>006_state_machine_snapshots.sql</c> and
/// <c>013_snapshot_draft_request_scope.sql</c>. Tests build them either from those migrations or via
/// <c>EnsureCreated</c> against a throwaway database.
/// </summary>
public sealed class SnapshotDbContext(DbContextOptions<SnapshotDbContext> options)
    : DbContext(options)
{
    /// <summary>
    /// The <c>snapshot_draft</c> table: one row per user's draft. Filter by <see cref="SnapshotRecord.UserKey"/>
    /// when querying directly, because the draft id alone is not unique across users.
    /// </summary>
    public DbSet<SnapshotRecord> SnapshotDrafts => Set<SnapshotRecord>();

    /// <summary>
    /// The <c>effect_claim</c> table: one row per exactly-once effect intent. A row with a null
    /// <see cref="EffectClaim.Receipt"/> is in flight; one with a receipt has run.
    /// </summary>
    public DbSet<EffectClaim> EffectClaims => Set<EffectClaim>();

    /// <summary>
    /// Maps both entities in the <c>trax</c> schema. On SQLite it drops the schema and stores the
    /// <c>jsonb</c> context as <c>TEXT</c>, so the model matches the SQLite migration.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("trax");
        SnapshotRecord.OnModelCreating(modelBuilder);
        EffectClaim.OnModelCreating(modelBuilder);

        // SQLite has no schemas and no jsonb type. Strip the "trax" schema and map jsonb -> TEXT so the
        // tables the stores query match the SQLite migration (006_state_machine_snapshots.sql: plain,
        // unqualified table names, TEXT columns). Mirrors Trax.Effect.Data.Sqlite's SqliteContext.
        // Detected by provider name to avoid a hard reference to the SQLite provider from this package.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                entityType.SetSchema(null);
                foreach (var property in entityType.GetProperties())
                {
                    if (property.GetColumnType() == "jsonb")
                        property.SetColumnType("TEXT");
                }
            }
        }
    }
}
