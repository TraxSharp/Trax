using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.PersistedOperation;

/// <summary>
/// Base model for <c>trax.persisted_operation</c>: a build-time-stable
/// GraphQL operation id mapping to a server-managed document. The mapping
/// lets shipped clients (mobile, etc.) hot-fix server-side queries without
/// a redeploy.
/// </summary>
/// <remarks>
/// EF Core mapping lives in
/// <see cref="Trax.Effect.Data.Models.PersistedOperation.PersistentPersistedOperation"/>.
/// <para>
/// <see cref="TenantKey"/> uses null at the C# boundary; storage layers
/// normalize null to the empty-string sentinel used by the database
/// composite primary key.
/// </para>
/// </remarks>
public class PersistedOperation
{
    /// <summary>
    /// Tenant scope. Null at the C# boundary; persisted as empty string to
    /// satisfy the composite primary key (Postgres disallows NULLs in PK columns).
    /// </summary>
    [Column("tenant_key")]
    public string? TenantKey { get; set; }

    /// <summary>
    /// Build-time-stable id (e.g. <c>userProfile_v1</c>).
    /// </summary>
    [Column("id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// Original GraphQL operation name (e.g. <c>UserProfile</c>).
    /// </summary>
    [Column("operation_name")]
    public string OperationName { get; set; } = null!;

    /// <summary>
    /// Numeric version extracted from the id suffix.
    /// </summary>
    [Column("version")]
    public int Version { get; set; }

    /// <summary>
    /// The GraphQL document text the id resolves to.
    /// </summary>
    [Column("document")]
    public string Document { get; set; } = null!;

    /// <summary>
    /// Canonicalized structural hash of the response shape (sha-256 hex).
    /// </summary>
    [Column("shape_fingerprint")]
    public string ShapeFingerprint { get; set; } = null!;

    /// <summary>
    /// True when the row is being served. False indicates a soft-delete.
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Required when <see cref="IsActive"/> is false.
    /// </summary>
    [Column("deprecation_reason")]
    public string? DeprecationReason { get; set; }

    /// <summary>
    /// Optional human-readable description shown to operators.
    /// </summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>
    /// When the row was first inserted.
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the row was last modified (upsert, deactivate, or restore).
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
