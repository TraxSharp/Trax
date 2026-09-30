using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.SnapshotDraft;

/// <summary>
/// Base model for <c>trax.snapshot_draft</c>: one persisted state-machine draft, scoped to a user. The four
/// snapshot fields are columns (with <c>context</c> a real Postgres <c>jsonb</c> column), plus an app-managed
/// optimistic-concurrency token and the request the last advance recorded.
/// </summary>
/// <remarks>
/// <para>EF Core mapping lives in <c>Trax.Effect.Data.Models.SnapshotDraft.PersistentSnapshotDraft</c> (in
/// Trax.Effect.Data). The table ships in the core migration set, and Trax.Effect.StateMachine.Persistence reaches
/// it through <c>IDataContext.SnapshotDrafts</c> rather than through a context or SQL of its own.</para>
///
/// <para><b>Identity is composite: <c>(user_key, machine, id)</c>.</b> The draft <c>id</c> is chosen by the
/// client (a machine may use one well-known id for a user's "current" instance), so it is unique only per user
/// and machine. A narrower key would let two users', or two machines', drafts share a row.</para>
/// </remarks>
public class SnapshotDraft
{
    /// <summary>The draft id — client-minted (a Guid), unique within a user and machine (see the composite key).</summary>
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>The owning user's key. A draft is only visible to (and mutable by) its owner.</summary>
    [Column("user_key")]
    public string UserKey { get; set; } = null!;

    /// <summary>
    /// The id of the machine this draft belongs to, part of the key. Not null.
    /// </summary>
    [Column("machine")]
    public string Machine { get; set; } = null!;

    /// <summary>
    /// The machine definition version the snapshot was written under, copied from the snapshot on every write.
    /// On load an older version is migrated forward through the machine's migrations; a newer one, or one with a
    /// gap in the chain, is refused as <c>version-mismatch</c>.
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
    /// The state the last applied advance fired from. A draft back in that state (reset, or moved back) no
    /// longer shows the request's outcome, so the same request id and trigger fire again rather than replay.
    /// </summary>
    [Column("last_request_from_state")]
    public string? LastRequestFromState { get; set; }

    /// <summary>
    /// When the draft was last written, set by the store to the UTC clock on every insert and update. With a
    /// draft TTL configured, a draft whose value is older than the TTL is deleted on its next load.
    /// </summary>
    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
