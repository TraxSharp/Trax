// What the examples on sdk-reference/graphql-api/cross-schema-data-loaders.md take as given: a
// loan in one context whose book lives in another.
global using Trax.Api.GraphQL.DataLoaders.CrossSchema;

public class Loan
{
    public int Id { get; set; }

    public int BookId { get; set; }
}

public class Book
{
    public int Id { get; set; }

    public string Title { get; set; } = "";
}

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
}
