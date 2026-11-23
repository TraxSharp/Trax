using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Entity gated with two stacked attributes. AND semantics across attributes:
/// the principal must hold <c>Admin</c> AND satisfy <c>AdminPolicy</c>. Both
/// reduce to the same underlying claim in this test host, so the practical
/// effect is "Admin only with both checks executed." The point of the test is
/// that both directives fire.
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Stacked-attribute restricted documents.")]
[TraxAuthorize(Roles = "Admin")]
[TraxAuthorize(Policy = "AdminPolicy")]
[Table("restricted_docs", Schema = "test_authz")]
public class RestrictedDoc
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("payload")]
    public string Payload { get; set; } = "";
}
