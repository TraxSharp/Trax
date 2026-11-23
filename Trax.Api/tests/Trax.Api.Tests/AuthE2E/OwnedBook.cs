using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Child entity gated with <c>[TraxAuthorize(Roles="Admin")]</c>. Direct access
/// (top-level <c>ownedBooks</c>) and transitive access (through
/// <c>owners[].books</c>) must both honor the role gate.
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Admin-only books.")]
[TraxAuthorize(Roles = "Admin")]
[Table("owned_books", Schema = "test_authz")]
public class OwnedBook
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("owner_id")]
    public long OwnerId { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    // Set by EF when the owner is loaded; reading it unloaded is a mistake in the test.
    public Owner Owner
    {
        get => field.NotNull(nameof(Owner));
        set;
    }
}
