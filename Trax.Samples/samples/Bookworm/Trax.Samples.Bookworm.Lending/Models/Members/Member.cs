using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.Bookworm.Lending.Models.Members;

/// <summary>
/// A library member. Owned by the lending domain, and the owner of per-member data: a member reads
/// only their own row (and so their own email), a librarian reads every row. The gate is a bare
/// <c>[TraxAuthorize]</c> (a caller must be authenticated); <see cref="Context.LendingDbContext"/>'s
/// query filter decides which rows.
/// </summary>
[TraxAuthorize]
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

    /// <summary>
    /// The principal id of the credential that acts as this member, as Trax qualifies it
    /// (<c>TraxApiKey:member</c>). Null for a member with no credential.
    /// </summary>
    [Column("principal_id")]
    public string? PrincipalId { get; set; }
}
