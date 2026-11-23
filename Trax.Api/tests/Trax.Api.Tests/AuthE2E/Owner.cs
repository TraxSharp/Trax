using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Test entities for type-level <c>[TraxAuthorize]</c> on <c>[TraxQueryModel]</c>.
/// Lives in its own <c>test_authz</c> schema so the data does not collide with
/// the unauthorized fixtures in <see cref="TestDbContext"/>.
///
/// The relationship between <see cref="Owner"/> and <see cref="OwnedBook"/> is
/// the centerpiece of the transitive-navigation security test: the parent
/// type is ungated, the child type carries <c>[TraxAuthorize(Roles="Admin")]</c>,
/// and the E2E suite verifies that a Player principal cannot reach the child
/// through the parent's <c>books</c> navigation property.
/// </summary>
[TraxAllowAnonymous]
[TraxQueryModel(Namespace = "vault", Description = "Owners of vault items (ungated).")]
[Table("owners", Schema = "test_authz")]
public class Owner
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    public IList<OwnedBook> Books { get; set; } = [];

    /// <summary>
    /// Navigation to anonymous-readable bulletin entries owned by this Owner.
    /// Owner is itself ungated; reaching <see cref="PublicBook"/> through this
    /// navigation must succeed for anonymous callers because both sides allow
    /// anonymous access.
    /// </summary>
    public IList<PublicBook> PublicBooks { get; set; } = [];
}
