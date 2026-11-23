using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Entity gated with bare <c>[TraxAuthorize]</c>: any authenticated user passes,
/// anonymous callers fail.
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Members-only area.")]
[TraxAuthorize]
[Table("member_areas", Schema = "test_authz")]
public class MemberArea
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";
}
