using Trax.Effect.Data.Models;

namespace Trax.Samples.Bookworm.Catalog.Models.Books;

/// <summary>
/// Scalar-only projection of <see cref="Book"/> for cross-schema reads. A context that does not own
/// the catalog schema reads books through this interface, never touching the <see cref="Book.Author"/>
/// navigation (which is ignored in the foreign model, see <see cref="Book.OnCrossSchemaModelCreating"/>).
/// </summary>
public interface IBookReference : IEntityReference
{
    int Id { get; }
    string Title { get; }
    string Isbn { get; }
    int AuthorId { get; }
}
