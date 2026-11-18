using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Entity opened to anonymous reads with <c>[TraxAllowAnonymous]</c>. The
/// E2E suite exercises three compositions that exist only because this entity
/// is anonymously readable:
/// <list type="bullet">
/// <item>Direct anonymous read of <c>publicBooks</c> succeeds.</item>
/// <item>Transitive <c>publicBook.linkedOwnedBook</c> still rejects anonymous —
/// the gated child's <c>@authorize</c> directive fires regardless of how the
/// parent opened the cascade. This is the Option B no-cascade contract.</item>
/// <item>Transitive <c>owner.publicBooks</c> succeeds for anonymous callers —
/// reaching an anonymous target through an ungated parent does not raise the
/// gate.</item>
/// </list>
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Public bulletin (anonymous-readable).")]
[TraxAllowAnonymous]
[Table("public_books", Schema = "test_authz")]
public class PublicBook
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("owner_id")]
    public long OwnerId { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    /// <summary>
    /// Nullable FK to a gated OwnedBook. The transitive-from-anonymous test
    /// requires at least one PublicBook with a real link; rows without a link
    /// prove the resolver does not over-eagerly join.
    /// </summary>
    [Column("linked_owned_book_id")]
    public long? LinkedOwnedBookId { get; set; }

    public Owner Owner { get; set; } = null!;

    public OwnedBook? LinkedOwnedBook { get; set; }
}
