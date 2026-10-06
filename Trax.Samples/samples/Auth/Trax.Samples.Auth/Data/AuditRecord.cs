using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.Auth.Data;

/// <summary>
/// One GraphQL request, as <c>Trax.Api.GraphQL.Audit</c> captured it and
/// <see cref="Audit.DatabaseAuditSink"/> stored it: who called, what they called, whether it was
/// allowed. Exposed as a query model only auditors can read.
/// </summary>
[TraxQueryModel(Namespace = "audit", Description = "The GraphQL audit trail (auditors only).")]
[TraxAuthorize(Roles = Auth.AuthRoles.Auditor)]
[Table("audit_records")]
public class AuditRecord
{
    public long Id { get; set; }

    /// <summary>
    /// <c>{scheme}:{id}</c>, for example <c>TraxApiKey:alice</c>, or <c>&lt;anonymous&gt;</c>.
    /// </summary>
    public string PrincipalId { get; set; } = "";

    /// <summary><c>apikey</c> or <c>jwt</c>; null for an anonymous caller.</summary>
    public string? PrincipalType { get; set; }

    public string? OperationName { get; set; }

    /// <summary>The document with every string and number literal blanked.</summary>
    public string Document { get; set; } = "";

    public bool Success { get; set; }

    public string? ErrorText { get; set; }

    public long DurationMs { get; set; }

    public DateTime Timestamp { get; set; }
}
