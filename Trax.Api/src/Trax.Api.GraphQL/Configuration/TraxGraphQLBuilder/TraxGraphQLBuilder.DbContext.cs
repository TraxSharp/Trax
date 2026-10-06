using Microsoft.EntityFrameworkCore;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    /// <summary>
    /// Registers a DbContext whose <c>DbSet&lt;T&gt;</c> entities marked with
    /// <c>[TraxQueryModel]</c> will be automatically exposed as paginated,
    /// filterable, sortable GraphQL queries under <c>discover</c>.
    /// </summary>
    /// <typeparam name="TDbContext">
    /// The DbContext type containing DbSet properties for the entities to expose.
    /// Must be registered in DI (e.g. via <c>AddDbContextFactory</c> or <c>AddDbContext</c>).
    /// </typeparam>
    public TraxGraphQLBuilder AddDbContext<TDbContext>()
        where TDbContext : DbContext
    {
        DbContextTypes.Add(typeof(TDbContext));
        return this;
    }
}
