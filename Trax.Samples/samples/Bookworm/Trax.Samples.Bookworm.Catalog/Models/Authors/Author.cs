using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.Bookworm.Catalog.Models.Authors;

/// <summary>
/// A book author. Owned by the catalog domain and exposed as a GraphQL query model.
/// </summary>
[TraxAllowAnonymous]
[TraxQueryModel(Namespace = GraphQLNamespaces.Catalog, Description = "Authors in the catalog")]
[Table("authors")]
public class Author
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;
}
