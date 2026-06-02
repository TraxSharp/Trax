using HotChocolate;
using HotChocolate.Types;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Samples.Bookworm.Catalog.Context;
using Trax.Samples.Bookworm.Catalog.Models.Books;
using Trax.Samples.Bookworm.Lending.Models.Loans;

namespace Trax.Samples.Bookworm.CrossSchema.Edges;

/// <summary>
/// Adds the <c>book</c> field to the GraphQL <c>Loan</c> type, resolving the catalog book a loan
/// references. The book lives in a different schema and context, so it is loaded through the batched
/// <see cref="CrossSchemaLoader{TContext, TEntity}"/> rather than a direct context query: every
/// <c>loan.book</c> in a request collapses into a single <c>WHERE id IN (...)</c> against the catalog.
/// </summary>
/// <remarks>
/// HotChocolate projection only materializes selected columns, so a query selecting <c>book</c> must
/// also select the loan's <c>bookId</c> for the foreign key to be present on the parent.
/// </remarks>
[ExtendObjectType(typeof(Loan))]
public sealed class LoanToBookEdge
{
    public async Task<Book?> GetBook(
        [Parent] Loan loan,
        CrossSchemaLoader<CatalogDbContext, Book> books,
        CancellationToken cancellationToken
    ) => await books.LoadAsync(loan.BookId, cancellationToken);
}
