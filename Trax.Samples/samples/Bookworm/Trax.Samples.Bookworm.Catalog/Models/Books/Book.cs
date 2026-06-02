using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;
using Trax.Samples.Bookworm.Catalog.Models.Authors;

namespace Trax.Samples.Bookworm.Catalog.Models.Books;

/// <summary>
/// A book in the catalog. Owned by the catalog domain. Exposed to GraphQL through
/// <see cref="IBookReference"/> (scalar fields only) so the <see cref="Author"/> navigation does not
/// leak into the schema, and so the cross-schema edge from a loan resolves to a stable scalar shape.
/// </summary>
[TraxAllowAnonymous]
[TraxQueryModel(
    Namespace = GraphQLNamespaces.Catalog,
    Description = "Books in the catalog",
    ExposeAs = typeof(IBookReference)
)]
[Table("books")]
public class Book : IBookReference
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("isbn")]
    public string Isbn { get; set; } = string.Empty;

    [Column("author_id")]
    public int AuthorId { get; set; }

    public Author? Author { get; set; }

    /// <summary>
    /// Registers <see cref="Book"/> as a read-only cross-schema entity in a context that does not own
    /// the catalog schema: maps it to the catalog <c>books</c> table, sets the key, and ignores every
    /// navigation so EF Core does not walk the catalog model graph into the foreign context (which
    /// would expect catalog tables to exist in that context's schema). This is the mechanism that
    /// prevents cross-domain model-graph leaks.
    /// </summary>
    public static void OnCrossSchemaModelCreating(ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.Entity<Book>(entity =>
        {
            entity.ToTable("books", schema);
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.Author);
        });
    }
}
