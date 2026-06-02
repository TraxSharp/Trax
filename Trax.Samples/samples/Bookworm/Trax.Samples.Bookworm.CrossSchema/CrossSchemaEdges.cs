using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Samples.Bookworm.Catalog.Context;
using Trax.Samples.Bookworm.Catalog.Models.Books;
using Trax.Samples.Bookworm.Lending.Models.Loans;

namespace Trax.Samples.Bookworm.CrossSchema;

/// <summary>
/// The single source of truth for every cross-schema GraphQL edge in Bookworm. Meta-tests reflect
/// over this list to verify each edge has a real integer FK on its source, a target owned by the
/// declared context, a camelCase field name, and a matching field in the checked-in schema.
/// </summary>
public static class CrossSchemaEdges
{
    public static readonly IReadOnlyList<CrossSchemaEdge> All =
    [
        // loan.book : a loan (lending schema) resolves the book it references (catalog schema).
        new(
            Source: typeof(Loan),
            Fk: nameof(Loan.BookId),
            Target: typeof(Book),
            TargetContext: typeof(CatalogDbContext),
            FieldName: "book"
        ),
    ];
}
