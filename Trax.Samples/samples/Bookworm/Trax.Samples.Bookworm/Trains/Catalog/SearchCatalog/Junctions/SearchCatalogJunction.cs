using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.Bookworm.Catalog.Context;

namespace Trax.Samples.Bookworm.Trains.Catalog.SearchCatalog.Junctions;

/// <summary>Reads matching books from the catalog context.</summary>
public class SearchCatalogJunction(ICatalogDbContext catalog)
    : Junction<SearchCatalogInput, SearchCatalogOutput>
{
    public override async Task<SearchCatalogOutput> Run(SearchCatalogInput input)
    {
        var query = input.Query.Trim();

        var books = await catalog
            .Books.Where(b => EF.Functions.ILike(b.Title, $"%{query}%"))
            .OrderBy(b => b.Title)
            .Select(b => new BookSummary
            {
                Id = b.Id,
                Title = b.Title,
                Isbn = b.Isbn,
            })
            .ToListAsync();

        return new SearchCatalogOutput { Books = books };
    }
}
