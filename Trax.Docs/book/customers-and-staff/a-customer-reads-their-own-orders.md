---
title: "6.5 A customer reads their own orders"
description: "Chapter 6 of the Trax book, section 5: A customer reads their own orders."
type: tutorial
area: general
parent: "6. Customers and staff"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/customers-and-staff/who-may-run-placeorder
---

# 6.5 A customer reads their own orders

`[TraxAuthorize]` decides who may run a train. It cannot decide which rows a query returns: a
customer may read orders, but only their own. That rule belongs to the data, so it is written once
on the context, as a query filter every query of `Orders` gets, rather than as a `Where` every
query has to remember.

The filter needs to know who is reading. The context depends on an interface for that, in `Data/`,
so the data layer does not depend on how callers sign in.

`Data/IShopfrontCaller.cs`:

```csharp compile=app file=Data/IShopfrontCaller.cs
namespace Shopfront.Data;

// Who is reading, as the shop's query filters see it.
public interface IShopfrontCaller
{
    string? CustomerId { get; }
    bool IsStaff { get; }
}
```

`Data/NoShopfrontCaller.cs`:

```csharp compile=app file=Data/NoShopfrontCaller.cs
namespace Shopfront.Data;

// Nobody: a context built with no caller, by a tool or at startup, sees no orders.
public sealed class NoShopfrontCaller : IShopfrontCaller
{
    public static readonly NoShopfrontCaller Instance = new();

    public string? CustomerId => null;
    public bool IsStaff => false;
}
```

The implementation reads Trax's view of the caller. `TraxCaller` never throws: with nobody signed
in, its `Principal` is null.

`Auth/TraxShopfrontCaller.cs`:

```csharp compile=app file=Auth/TraxShopfrontCaller.cs
using Shopfront.Data;
using Trax.Api.Auth;

namespace Shopfront.Auth;

public sealed class TraxShopfrontCaller(TraxCaller caller) : IShopfrontCaller
{
    public string? CustomerId => caller.Principal?.Id;
    public bool IsStaff => caller.Principal?.Roles.Contains(Roles.Staff) == true;
}
```

`Data/ShopfrontDbContext.cs`:

```csharp compile=app file=Data/ShopfrontDbContext.cs
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
```

The filter is written against the field `caller`, and Entity Framework reads that field again for
each context, so every context filters for its own caller. A context with no caller sees no orders
at all: the filter fails closed.

`[ActivatorUtilitiesConstructor]` tells the container which constructor to use. The other one is
for code that builds a context by hand, such as the tests and the guard that reads the model.

A context that holds its caller cannot come from a pool, which builds contexts from their options
alone and reuses them across callers. It is registered as a factory per scope instead.

`Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Fraud;
using Shopfront.Inventory;
using Shopfront.Payments;
using Trax.Core.Decisions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Extensions;

public static class ShopfrontServiceCollectionExtensions
{
    // Everything Shopfront's trains need, apart from Trax itself.
    public static IServiceCollection AddShopfront(
        this IServiceCollection services,
        string connectionString
    ) =>
        services
            // One context per scope, built for that scope's caller: a pool would share it.
            .AddDbContextFactory<ShopfrontDbContext>(
                options => options.UseNpgsql(connectionString),
                ServiceLifetime.Scoped
            )
            .AddScoped<IShopfrontDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<ShopfrontDbContext>>().CreateDbContext()
            )
            .AddScoped<IShopfrontCaller, TraxShopfrontCaller>()
            .AddScoped<Catalogue>()
            .AddSingleton<IPaymentProvider, FakePaymentProvider>()
            .AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>()
            .AddSingleton<IDecider>(FraudRules.Live())
            .AddSingleton<ICandidateFraudDecider, StricterFraudRules>();
}
```

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)

---

Next: [6.6 Sign in, and run trains as the caller](/docs/book/customers-and-staff/sign-in-and-run-trains-as-the-caller)
