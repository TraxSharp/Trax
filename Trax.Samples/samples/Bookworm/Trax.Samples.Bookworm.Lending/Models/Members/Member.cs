using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.Bookworm.Lending.Models.Members;

/// <summary>A library member. Owned by the lending domain.</summary>
[TraxAllowAnonymous]
[TraxQueryModel(Namespace = GraphQLNamespaces.Lending, Description = "Library members")]
[Table("members")]
public class Member
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("email")]
    public string Email { get; set; } = string.Empty;
}
