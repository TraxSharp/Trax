using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Enums;

namespace Trax.Effect.Models.SnapshotDraft;

/// <summary>
/// Base model for <c>trax.snapshot_draft</c>: one persisted state-machine draft, owned by a user or by the system. The four
/// snapshot fields are columns (with <c>context</c> a real Postgres <c>jsonb</c> column), plus an app-managed
/// optimistic-concurrency token and the request the last advance recorded.
/// </summary>
/// <remarks>
/// <para>EF Core mapping lives in <c>Trax.Effect.Data.Models.SnapshotDraft.PersistentSnapshotDraft</c> (in
/// Trax.Effect.Data). The table ships in the core migration set, and Trax.Effect.StateMachine.Persistence reaches
/// it through <c>IDataContext.SnapshotDrafts</c> rather than through a context or SQL of its own.</para>
///
/// <para><b>Identity names the owner kind.</b> A user's draft is unique by <c>(user_key, machine, id)</c>: its
/// <c>id</c> is chosen by the client (a machine may use one well-known id for a user's "current" instance), so it
/// is unique only per user and machine. A system-owned instance has no user key and is unique by
/// <c>(machine, id)</c> among system rows. A user may hold the same id as a system instance, so every lookup by id
/// names the owner kind as well. The primary key is the surrogate <see cref="RowId"/>, because a key column
/// cannot be null and a system row's <c>user_key</c> is.</para>
/// </remarks>
public class SnapshotDraft
{
    /// <summary>
    /// The row's surrogate primary key, assigned by the database and never reused. Not an identity anyone looks a
    /// draft up by: that is the owner kind, the owner, the machine and <see cref="Id"/>. It names one incarnation of
    /// the instance: a draft deleted and created again under the same id is a new row with a new row id.
    /// </summary>
    [Column("row_id")]
    public long RowId { get; set; }

    /// <summary>
    /// Who owns the row. A <see cref="SnapshotOwnerKind.User"/> row has a <see cref="UserKey"/>; a
    /// <see cref="SnapshotOwnerKind.System"/> row has none, and no user request reaches it.
    /// </summary>
    [Column("owner_kind")]
    public SnapshotOwnerKind OwnerKind { get; set; } = SnapshotOwnerKind.User;

    /// <summary>The draft id — client-minted (a Guid), unique within a user and machine (see the composite key).</summary>
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// The owning user's key, or null on a system-owned row. A user's draft is only visible to (and mutable by)
    /// its owner.
    /// </summary>
    [Column("user_key")]
    public string? UserKey { get; set; }

    /// <summary>
    /// The id of the machine this draft belongs to, part of the key. Not null.
    /// </summary>
    [Column("machine")]
    public string Machine { get; set; } = null!;

    /// <summary>
    /// The machine definition version the snapshot was written under, copied from the snapshot on every write.
    /// On load an older version is migrated forward through the machine's migrations; a newer one, or one with a
    /// gap in the chain, is refused as <c>VERSION_MISMATCH</c>.
    /// </summary>
    [Column("version")]
    public int Version { get; set; }

    /// <summary>The current state's name (the <c>TState</c> enum member as text). Not null.</summary>
    [Column("state")]
    public string State { get; set; } = null!;

    /// <summary>The per-state context, stored as a genuine <c>jsonb</c> column (queryable server-side).</summary>
    [Column("context", TypeName = "jsonb")]
    public string Context { get; set; } = "{}";

    /// <summary>
    /// App-managed optimistic-concurrency token (a fresh Guid on every write). Mapped as a concurrency token, so
    /// a stale tracked write throws <c>DbUpdateConcurrencyException</c>; the atomic update path guards on it in a
    /// WHERE clause instead. Provider-agnostic, unlike Postgres <c>xmin</c>.
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
    /// The state the last applied advance fired from. A row with no ring reads its last request from this and the
    /// columns beside it, and a retry of that request replays wherever the draft is now.
    /// </summary>
    [Column("last_request_from_state")]
    public string? LastRequestFromState { get; set; }

    /// <summary>
    /// When the row was created, set by the store to the UTC clock on insert and never changed after. Null on a
    /// row written before the column existed, or by a host that predates it: nothing recorded when those were
    /// created, and <see cref="UpdatedAt"/> is when they last changed, not when they began.
    /// </summary>
    [Column("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// When the draft was last written, set by the store to the UTC clock on every insert and update. With a
    /// draft TTL configured, a draft whose value is older than the TTL is deleted on its next load.
    /// </summary>
    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// The correlation token of the train run the current state invoked, or null when no run is live. Set to the
    /// queued run's external id when an invoking state is entered and cleared when it is left; an outcome is
    /// applied only to the row that still carries its token. Server-only: it is never part of the snapshot a
    /// client reads or writes. Unique where set.
    /// </summary>
    [Column("invoke_token")]
    public string? InvokeToken { get; set; }

    /// <summary>
    /// The invoking state the instance was stranded in, or null. Set when a run the state invoked ended and not
    /// even its failure could be applied: the token is cleared and the instance stays in the state with no live
    /// run, leaving it only through one of its declared transitions. The mark holds while the row is in that state
    /// with no token; a write that gives the row a token, or moves it to another state, clears it. Server-only.
    /// </summary>
    [Column("invoke_stranded_state")]
    public string? InvokeStrandedState { get; set; }

    /// <summary>
    /// The instance's revision: 1 when the row is inserted, and one more on every update of it. The database bumps
    /// it in a <c>BEFORE UPDATE</c> trigger (an <c>AFTER UPDATE</c> one on SQLite), so a write that leaves it out,
    /// from a custom store or raw SQL, is numbered too. A deleted and re-created instance is a new row, with a new
    /// <see cref="RowId"/>, and starts again at 1: revisions are ordered within one <see cref="RowId"/> only.
    /// Server-only: it travels beside the snapshot, never inside it.
    /// </summary>
    [Column("revision")]
    public long Revision { get; set; } = 1;

    /// <summary>
    /// The hash of the IR of the machine definition that last wrote the row (<c>IMachine.SchemaHash</c>), or null
    /// for a machine with no exportable IR and for a writer that does not record it. Server-only.
    /// </summary>
    [Column("ir_hash")]
    public string? IrHash { get; set; }

    /// <summary>
    /// The client's request id of the <c>start</c> that created the row, or null for a row a save or host code
    /// created. Unique per owner kind, user and machine (a partial index the migrations create), so a retried start
    /// finds the instance it created. Server-only.
    /// </summary>
    [Column("start_request_id")]
    public string? StartRequestId { get; set; }

    /// <summary>
    /// The fingerprint of the input of the start that created the row, so one request id with a different input is
    /// told apart. Null when <see cref="StartRequestId"/> is. Server-only.
    /// </summary>
    [Column("start_fingerprint")]
    public string? StartFingerprint { get; set; }

    /// <summary>
    /// The row's most recent client requests as a JSON array, oldest first: each one's request id, a fingerprint of
    /// its trigger and input, the state it fired from, the revision it produced and the snapshot it was answered
    /// with. A retry is answered from here. Writes the server makes (an outcome, a timer) never change it. Null on a
    /// row no client request has advanced since it was added. Server-only.
    /// </summary>
    [Column("recent_requests", TypeName = "jsonb")]
    public string? RecentRequests { get; set; }

    /// <summary>
    /// When the timer of the state the instance is in fires, in UTC, or null when that state has no timer. Every write
    /// that enters a timed state sets it, in the same statement as the state, from the database's clock plus the
    /// state's duration; a write that leaves the state clears it, and one that stays in it keeps the time set at entry.
    /// The hosts that register the machine fire the rows whose time has passed. A partial index the migrations create
    /// (<c>ix_snapshot_draft_due</c>) serves that sweep. Server-only: it travels beside the snapshot, never inside it.
    /// </summary>
    [Column("due_at")]
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>
    /// The claimant of the effect pending on the row, or null when none is. Sending an effect-bound event sets it by a
    /// conditional write; every other write requires it to be null, so nothing moves or changes the row while the effect
    /// runs, and the effect's receipt, the move to its target and clearing it are one write guarded by it.
    /// Server-only.
    /// </summary>
    [Column("effect_token")]
    public Guid? EffectToken { get; set; }

    /// <summary>
    /// The key the pending effect runs under, minted when the row is marked: a run again after its claimant stopped uses
    /// it, and the next send of the event mints a new one. Null when no effect is pending. Server-only.
    /// </summary>
    [Column("effect_key")]
    public string? EffectKey { get; set; }

    /// <summary>The event whose effect is pending on the row, or null when none is. Server-only.</summary>
    [Column("effect_trigger")]
    public string? EffectTrigger { get; set; }

    /// <summary>
    /// The client request that sent the pending effect, or null when none is pending, so whoever finishes the effect
    /// records it as that request's answer. Server-only.
    /// </summary>
    [Column("effect_request_id")]
    public string? EffectRequestId { get; set; }

    /// <summary>
    /// When the pending effect's claimant's lease ends, on the database's clock, or null when none is pending. Past it,
    /// the next send or the reconciler takes the mark over and runs the effect again under the same key. A partial
    /// index the migrations create (<c>ix_snapshot_draft_effect_pending</c>) serves that read. Server-only.
    /// </summary>
    [Column("effect_lease_until")]
    public DateTimeOffset? EffectLeaseUntil { get; set; }

    /// <summary>
    /// Whether the server put the row in its state: true after an effect, an invoked train's outcome or an event gated
    /// by a role or a policy; false after a client's event, start or save; carried over by a timer. It decides what
    /// discard refuses, what the draft sweep keeps and what the draft cap counts. Null on a row written before it was
    /// recorded, or by host code through the store, which is judged by its state as before. Server-only.
    /// </summary>
    [Column("server_record")]
    public bool? ServerRecord { get; set; }
}
