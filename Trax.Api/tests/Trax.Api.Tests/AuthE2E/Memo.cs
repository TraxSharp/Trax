using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Entity gated with a CSV role list (<c>Roles="Admin,Player"</c>). Both roles
/// satisfy the gate (OR semantics within a single attribute).
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Memos visible to Admin or Player.")]
[TraxAuthorize(Roles = "Admin,Player")]
[Table("memos", Schema = "test_authz")]
public class Memo
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("body")]
    public string Body { get; set; } = "";
}
