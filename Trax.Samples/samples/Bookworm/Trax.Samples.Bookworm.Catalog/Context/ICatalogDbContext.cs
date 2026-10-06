using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Bookworm.Catalog.Models.Authors;
using Trax.Samples.Bookworm.Catalog.Models.Books;

namespace Trax.Samples.Bookworm.Catalog.Context;

/// <summary>Companion interface for <see cref="CatalogDbContext"/>.</summary>
public interface ICatalogDbContext : IDomainDataContext
{
    DbSet<Author> Authors { get; }
    DbSet<Book> Books { get; }
}
