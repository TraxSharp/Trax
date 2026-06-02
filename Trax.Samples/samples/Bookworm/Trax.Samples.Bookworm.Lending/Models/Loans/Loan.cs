using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.Bookworm.Lending.Models.Loans;

/// <summary>
/// A book loan. Owned by the lending domain. <see cref="BookId"/> references a book owned by the
/// catalog domain (a different schema and context), so it is a plain integer here, never an EF
/// navigation: the lending domain stays isolated from the catalog domain. The GraphQL <c>book</c>
/// field that resolves the referenced book is added by the cross-schema edge project, not here.
/// </summary>
[TraxQueryModel(Namespace = GraphQLNamespaces.Lending, Description = "Book loans")]
[Table("loans")]
public class Loan
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("member_id")]
    public int MemberId { get; set; }

    /// <summary>Foreign key into the catalog domain's <c>books</c> table. Resolved cross-schema in GraphQL.</summary>
    [Column("book_id")]
    public int BookId { get; set; }

    [Column("borrowed_at")]
    public DateTime BorrowedAt { get; set; }

    [Column("returned_at")]
    public DateTime? ReturnedAt { get; set; }
}
