// What the compiled examples on effect/move-a-domain-data-context-to-ef-migrations.md take as
// given: the context set up as in effect/effect-providers/domain-data-contexts.md.
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

public class Order
{
    public int Id { get; set; }
}

public interface IShopDbContext : IDomainDataContext
{
    DbSet<Order> Orders { get; }
}

public class ShopDbContext(DbContextOptions<ShopDbContext> options)
    : DomainDataContext<ShopDbContext>(options),
        IShopDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override string Schema => "shop";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { }
}
