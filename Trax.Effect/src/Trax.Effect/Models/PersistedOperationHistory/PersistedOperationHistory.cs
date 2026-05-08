using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.PersistedOperationHistory;

/// <summary>
/// One row per change to a row in <c>trax.persisted_operation</c>. Used for
/// audit and rollback; never read on the request path.
/// </summary>
/// <remarks>
/// EF Core mapping lives in
/// <see cref="Trax.Effect.Data.Models.PersistedOperationHistory.PersistentPersistedOperationHistory"/>.
/// </remarks>
public class PersistedOperationHistory
{
    /// <summary>
    /// Auto-incrementing surrogate. <c>bigserial</c> in Postgres.
    /// </summary>
    [Column("history_id")]
    public long HistoryId { get; set; }

    /// <summary>
    /// Tenant scope. Null at the C# boundary; persisted as empty string.
    /// </summary>
    [Column("tenant_key")]
    public string? TenantKey { get; set; }

    /// <summary>
    /// Mirrors the live row's id.
    /// </summary>
    [Column("id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// Snapshot of the document text at the time of the change.
    /// </summary>
    [Column("document")]
    public string Document { get; set; } = null!;

    /// <summary>
    /// Snapshot of the shape fingerprint at the time of the change.
    /// </summary>
    [Column("shape_fingerprint")]
    public string ShapeFingerprint { get; set; } = null!;

    /// <summary>
    /// One of <c>Upsert</c>, <c>Deactivate</c>, <c>Restore</c>.
    /// </summary>
    [Column("change_type")]
    public string ChangeType { get; set; } = null!;

    /// <summary>
    /// When the change happened.
    /// </summary>
    [Column("changed_at")]
    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// Required on deactivate, optional on upsert/restore.
    /// </summary>
    [Column("changed_reason")]
    public string? ChangedReason { get; set; }
}
