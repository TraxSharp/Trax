using Microsoft.EntityFrameworkCore;
using Shopfront.Data.Models;
using Trax.Effect.Data.Services.DomainContext;

namespace Shopfront.Data;

public class ShopfrontDbContext : DomainDataContext<ShopfrontDbContext>, IShopfrontDbContext
{
    private readonly IShopfrontCaller caller;

    [ActivatorUtilitiesConstructor]
    public ShopfrontDbContext(DbContextOptions<ShopfrontDbContext> options, IShopfrontCaller caller)
        : base(options) => this.caller = caller;

    public ShopfrontDbContext(DbContextOptions<ShopfrontDbContext> options)
        : this(options, NoShopfrontCaller.Instance) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    protected override string Schema => "shop";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.HasKey(p => p.Sku);
            product.Property(p => p.Price).HasPrecision(10, 2);
            product.HasData(
                new Product
                {
                    Sku = "MUG",
                    Name = "Mug",
                    Price = 12.00m,
                    Stock = 10,
                },
                new Product
                {
                    Sku = "TEE",
                    Name = "T-shirt",
                    Price = 25.00m,
                    Stock = 3,
                },
                new Product
                {
                    Sku = "CAP",
                    Name = "Cap",
                    Price = 18.50m,
                    Stock = 0,
                }
            );
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.Property(o => o.Total).HasPrecision(10, 2);
            order.HasIndex(o => o.CustomerId);
            order.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            // A customer reads their own orders; staff read them all; nobody else reads any.
            order.HasQueryFilter(o =>
                caller.IsStaff || (caller.CustomerId != null && o.CustomerId == caller.CustomerId)
            );
        });

        modelBuilder.Entity<OrderItem>(item =>
        {
            item.Property(i => i.UnitPrice).HasPrecision(10, 2);
            // An item is readable exactly when its order is.
            item.HasQueryFilter(i =>
                caller.IsStaff
                || Set<Order>()
                    .Any(o =>
                        o.Id == i.OrderId
                        && caller.CustomerId != null
                        && o.CustomerId == caller.CustomerId
                    )
            );
        });
    }
}
