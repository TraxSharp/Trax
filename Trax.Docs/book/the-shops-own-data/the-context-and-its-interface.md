---
title: "4.4 The context and its interface"
description: "Chapter 4 of the Trax book, section 4: The context and its interface."
type: tutorial
area: general
parent: "4. The shop's own data"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/the-shops-own-data/the-models
---

# 4.4 The context and its interface

`Data/IShopfrontDbContext.cs`:

```csharp compile=app file=Data/IShopfrontDbContext.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data.Models;
using Trax.Effect.Data.Services.DomainContext;

namespace Shopfront.Data;

public interface IShopfrontDbContext : IDomainDataContext
{
    DbSet<Product> Products { get; }
    DbSet<Order> Orders { get; }
}
```

`Data/ShopfrontDbContext.cs`:

```csharp compile=app file=Data/ShopfrontDbContext.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data.Models;
using Trax.Effect.Data.Services.DomainContext;

namespace Shopfront.Data;

public class ShopfrontDbContext(DbContextOptions<ShopfrontDbContext> options)
    : DomainDataContext<ShopfrontDbContext>(options),
        IShopfrontDbContext
{
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
        });

        modelBuilder.Entity<OrderItem>().Property(i => i.UnitPrice).HasPrecision(10, 2);
    }
}
```

`Schema` puts every table in `shop`. `ConfigureModel` is where the model is configured: the base
class keeps `OnModelCreating` to itself, so that it can apply the schema and store every
`DateTime` as UTC around what `ConfigureModel` does. The catalogue chapter 1 kept in a dictionary
is now seed data, written when the table is created.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [4.5 The catalogue reads the database](/docs/book/the-shops-own-data/the-catalogue-reads-the-database)
