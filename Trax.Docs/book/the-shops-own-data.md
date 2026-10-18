---
title: "4. The shop's own data"
description: "Chapter 4 of the Trax book: give Shopfront its own Postgres schema with a domain data context for products, stock and orders, and test against a real database."
type: tutorial
area: general
parent: The Trax book
nav_order: 14
snippet_context: none
follows: book/every-order-is-recorded
---

# 4. The shop's own data

Trax keeps its records in its own schema, `trax`. Shopfront's own data, what it sells, how many of
each it has and the orders customers placed, has lived in a dictionary and nowhere at all. In this
chapter Shopfront gets a schema of its own beside Trax's, `shop`, with products and orders, and
`PlaceOrder` reserves stock and stores each confirmed order there.

This chapter starts from the end of [chapter 3](/docs/book/every-order-is-recorded). It uses the
same Postgres container.

## One context, one schema

Trax's own tables belong to Trax's data context. An application's data goes in a context of its
own, derived from `DomainDataContext<TSelf>`, which holds to one rule: one context, one Postgres
schema. Code that uses the data depends on an interface beside the context, never on the context
itself.

By the end of the chapter the new files are:

```text
Shopfront/
└── Data/
    ├── IShopfrontDbContext.cs
    ├── ShopfrontDbContext.cs
    └── Models/
        ├── Product.cs
        ├── Order.cs
        └── OrderItem.cs
```

## Packages

The context uses Entity Framework Core's Postgres provider directly now, so the application names
it rather than relying on Trax to bring it. The tests add Trax's data-layer guards.

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Core" Version="1.61.0" />
    <PackageVersion Include="Trax.Core.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Postgres" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Logging" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator.Testing" Version="1.61.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="AwesomeAssertions" Version="9.6.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="NUnit" Version="5.0.0" />
    <PackageVersion Include="NUnit.Analyzers" Version="4.15.0" />
    <PackageVersion Include="NUnit3TestAdapter" Version="6.3.0" />
  </ItemGroup>
</Project>
```

```xml file=Shopfront.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- The tests live under tests/, in a project of their own. -->
    <DefaultItemExcludes>$(DefaultItemExcludes);tests/**</DefaultItemExcludes>
  </PropertyGroup>

  <ItemGroup>
    <!-- The junctions are internal; their tests are the one project that may see them. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.Postgres" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>
</Project>
```

```xml file=tests/Shopfront.Tests/Shopfront.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Shopfront.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit.Analyzers" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Trax.Core.Testing" />
    <PackageReference Include="Trax.Effect.Data.InMemory" />
    <PackageReference Include="Trax.Effect.Data.Testing" />
    <PackageReference Include="Trax.Mediator.Testing" />
  </ItemGroup>
</Project>
```

## The models

`Data/Models/Product.cs`:

```csharp compile=app file=Data/Models/Product.cs
using System.ComponentModel.DataAnnotations;

namespace Shopfront.Data.Models;

public class Product
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }

    // Postgres's row version. A save that finds the row changed since it was read is refused.
    [Timestamp]
    public uint Version { get; set; }
}
```

Two customers can order the last T-shirt at the same moment. Both read a stock of one, both
take one, and both save a stock of zero: one T-shirt sold twice. `Version` stops that. On Postgres a
`uint` marked `[Timestamp]` is the row's system version, `xmin`, which changes on every write.
Entity Framework adds it to the update's `WHERE`, so the second save matches no row and is refused
with a `DbUpdateConcurrencyException` instead of overwriting the first.

`Data/Models/Order.cs`:

```csharp compile=app file=Data/Models/Order.cs
namespace Shopfront.Data.Models;

public class Order
{
    public Guid Id { get; set; }
    public required string CustomerId { get; set; }
    public decimal Total { get; set; }
    public required string TransactionId { get; set; }
    public DateTime PlacedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}
```

`Data/Models/OrderItem.cs`:

```csharp compile=app file=Data/Models/OrderItem.cs
namespace Shopfront.Data.Models;

public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

An order keeps the price each item was sold at, so a later change to the catalogue does not
rewrite what a customer paid. The receipt link is not stored: the customer has it, and the shop
does not need it.

## The context and its interface

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

## The catalogue reads the database

The catalogue stays where chapter 1 put it, outside `Trains/`, because more than one train will
use it. It keeps its job and changes where it does it.

`Inventory/Catalogue.cs`:

```csharp compile=app file=Inventory/Catalogue.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;

namespace Shopfront.Inventory;

// What the shop sells and how many of each it has.
public class Catalogue(IShopfrontDbContext db)
{
    // The price of each of these SKUs that the shop sells.
    public Task<Dictionary<string, decimal>> PricesOf(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken
    ) =>
        db.Products.Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, p => p.Price, cancellationToken);

    // Reserves every line or none of them. Returns the first SKU that is short, or null.
    public async Task<string?> TryReserve(
        IReadOnlyList<(string Sku, int Quantity)> lines,
        CancellationToken cancellationToken
    )
    {
        var skus = lines.Select(l => l.Sku).ToList();
        var products = await db
            .Products.Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, cancellationToken);

        var shortSku = lines
            .Where(l => products[l.Sku].Stock < l.Quantity)
            .Select(l => l.Sku)
            .FirstOrDefault();
        if (shortSku is not null)
            return shortSku;

        foreach (var (sku, quantity) in lines)
            products[sku].Stock -= quantity;

        // One transaction for every line. If another order changed one of these products since
        // it was read, nothing is saved and DbUpdateConcurrencyException says so.
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // Puts reserved stock back. Each line is one atomic increment, so it never overwrites
    // another order's change.
    public async Task Release(
        IReadOnlyList<(string Sku, int Quantity)> lines,
        CancellationToken cancellationToken
    )
    {
        foreach (var (sku, quantity) in lines)
            await db
                .Products.Where(p => p.Sku == sku)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(p => p.Stock, p => p.Stock + quantity),
                    cancellationToken
                );
    }
}
```

The catalogue now holds a data context, which lives as long as one unit of work, so it is
registered per scope rather than once. Trax runs each train in a scope of its own, so every run of
`PlaceOrder` gets its own catalogue and context.

## The junctions follow

Pricing asks the catalogue for every price at once.

`Trains/PlaceOrder/Junctions/PriceCartJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/PriceCartJunction.cs
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class PriceCartJunction(Catalogue catalogue, ILogger<PriceCartJunction> logger)
    : EffectJunction<PlaceOrderInput, PricedCart>
{
    public override async Task<PricedCart> Run(PlaceOrderInput input)
    {
        if (input.Lines.Count == 0)
            throw new TrainException("The cart is empty.");

        var prices = await catalogue.PricesOf(
            [.. input.Lines.Select(l => l.Sku)],
            CancellationToken
        );

        var lines = input
            .Lines.Select(line =>
            {
                if (line.Quantity < 1)
                    throw new TrainException($"{line.Sku}: the quantity must be at least 1.");
                if (!prices.TryGetValue(line.Sku, out var price))
                    throw new TrainException($"{line.Sku} is not in the catalogue.");
                return new PricedLine(line.Sku, line.Quantity, price);
            })
            .ToList();

        var cart = new PricedCart(lines, lines.Sum(l => l.UnitPrice * l.Quantity));
        logger.LogInformation("Priced {Count} lines at {Total}", lines.Count, cart.Total);
        return cart;
    }
}
```

`Trains/PlaceOrder/Junctions/ReserveStockJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ReserveStockJunction.cs
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ReserveStockJunction(Catalogue catalogue, ILogger<ReserveStockJunction> logger)
    : EffectJunction<PricedCart, StockReservation>
{
    public override async Task<StockReservation> Run(PricedCart cart)
    {
        var shortSku = await catalogue.TryReserve(
            [.. cart.Lines.Select(l => (l.Sku, l.Quantity))],
            CancellationToken
        );
        if (shortSku is not null)
            throw new TrainException($"{shortSku} is out of stock.");

        var reservation = new StockReservation(Guid.NewGuid());
        logger.LogInformation("Reserved stock as {ReservationId}", reservation.Id);
        return reservation;
    }
}
```

The payment junction gives the stock back with `CancellationToken.None`, not the run's token: a
customer who gives up on the request has cancelled the order, and the stock still has to go back.

`Trains/PlaceOrder/Junctions/TakePaymentJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/TakePaymentJunction.cs
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class TakePaymentJunction(
    IPaymentProvider payments,
    Catalogue catalogue,
    ILogger<TakePaymentJunction> logger
) : EffectJunction<(PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation), Payment>
{
    public override async Task<Payment> Run(
        (PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation) input
    )
    {
        ChargeResult charge;
        try
        {
            charge = await payments.Charge(
                input.Order.CustomerId,
                input.Order.PaymentToken,
                input.Cart.Total,
                CancellationToken
            );
        }
        catch (Exception ex)
        {
            // However the charge failed, the order stops here, so its stock goes back, even when
            // the run itself was cancelled.
            await catalogue.Release(
                [.. input.Cart.Lines.Select(l => (l.Sku, l.Quantity))],
                CancellationToken.None
            );
            logger.LogInformation("Released reservation {ReservationId}", input.Reservation.Id);

            if (ex is PaymentDeclinedException)
                throw new TrainException(ex.Message);
            throw;
        }

        logger.LogInformation(
            "Charged {Total} as {TransactionId}",
            input.Cart.Total,
            charge.TransactionId
        );
        return new Payment(charge.TransactionId, input.Cart.Total, charge.ReceiptUrl);
    }
}
```

Confirming the order now stores it. The junction asks Memory for the input as well, for the
customer.

`Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Data;
using Shopfront.Data.Models;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(IShopfrontDbContext db, ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(PlaceOrderInput Input, PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override async Task<PlaceOrderOutput> Run(
        (PlaceOrderInput Input, PricedCart Cart, Payment Payment) input
    )
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = input.Input.CustomerId,
            Total = input.Cart.Total,
            TransactionId = input.Payment.TransactionId,
            PlacedAt = DateTime.UtcNow,
            Items =
            [
                .. input.Cart.Lines.Select(l => new OrderItem
                {
                    Sku = l.Sku,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                }),
            ],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(CancellationToken);

        logger.LogInformation("Confirmed order {OrderId}", order.Id);
        return new PlaceOrderOutput(
            order.Id,
            order.Total,
            order.TransactionId,
            input.Payment.ReceiptUrl
        );
    }
}
```

The order is stored after the customer is charged, so a database that fails at exactly this
point leaves a payment with no order. Charging only once, however often an order is tried, comes
later in the book.

## Two orders, one T-shirt

The concurrency check turns a lost race into a `DbUpdateConcurrencyException`. Running the same
order again would read the new stock and could succeed, so it is a transient failure.

`Failures/ShopfrontFailureClassifier.cs`:

```csharp compile=app file=Failures/ShopfrontFailureClassifier.cs
using Microsoft.EntityFrameworkCore;
using Trax.Core.Exceptions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Failures;

public class ShopfrontFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            // A message for the customer: the same order fails the same way however often it runs.
            TrainException => FailureClass.Permanent,
            // The provider did not answer this time; it may next time.
            TimeoutException => FailureClass.Transient,
            // Another order changed the stock first; trying again reads the new stock.
            DbUpdateConcurrencyException => FailureClass.Transient,
            _ => null,
        };
}
```

## Register the context and create its schema

`Program.cs`:

```csharp compile=app file=Program.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.FailureClassifier;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddDomainDataContext<IShopfrontDbContext, ShopfrontDbContext>(options =>
    options.UseNpgsql(connectionString)
);
builder.Services.AddScoped<Catalogue>();
builder.Services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
builder.Services.AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
        )
        .AddMediator(typeof(Program).Assembly)
);

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.MapPost(
    "/orders",
    async (PlaceOrderInput input, IPlaceOrderTrain train, CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await train.Run(input, cancellationToken));
        }
        catch (TrainException ex)
        {
            return Results.UnprocessableEntity(ex.Message);
        }
    }
);

await app.RunAsync();
```

`AddDomainDataContext` registers a pool of contexts and resolves `IShopfrontDbContext` from it, one
per scope. Both contexts share the one database: Trax's tables in `trax`, Shopfront's in `shop`.

`EnsureSchemaCreatedAsync` runs once the host is built. It creates the `shop` schema and any table
the model has that the database does not, with its seed rows, and leaves every existing table as it
is. That suits a first deployment. It compares tables by name only, so when a later chapter changes
a table's columns, the development database has to be recreated, or the context moved to
[EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations).

## Run it

Start the application and place an order:

```bash
dotnet run --urls http://localhost:5080
```

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":2},{"sku":"TEE","quantity":1}]}'
```

The stock and the order are in the `shop` schema:

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront \
  -c 'select "Sku", "Stock" from shop."Products" order by "Sku"' \
  -c 'select "CustomerId", "Total", "TransactionId" from shop."Orders"'
```

```text
 Sku | Stock
-----+-------
 CAP |     0
 MUG |     8
 TEE |     2
(3 rows)

 CustomerId | Total | TransactionId
------------+-------+---------------
 ada        | 49.00 | tx_d8a78340
(1 row)
```

Stop the application and start it again. The stock is still eight mugs and two T-shirts, where
before every restart refilled the shelves.

## Test against a real database

The junction tests in chapter 2 needed no database. Now pricing and reserving are queries, and a
test that replaces the database with a fake would test the fake. They run against a database of
their own, `shopfront_tests`, on the same Postgres, which each test drops and creates again, so it
starts from the seeded catalogue with no orders.

`tests/Shopfront.Tests/TestDatabase.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/TestDatabase.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;

namespace Shopfront.Tests;

// The tests' own database, beside the application's, so a test never touches its rows.
internal static class TestDatabase
{
    public const string ConnectionString =
        "Host=localhost;Database=shopfront_tests;Username=postgres;Password=shopfront";

    public static ShopfrontDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ShopfrontDbContext>().UseNpgsql(ConnectionString).Options);

    // Drops and creates the database: the seeded catalogue, and no orders or runs.
    public static async Task Reset()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public static async Task<int> StockOf(string sku)
    {
        await using var db = NewContext();
        return await db.Products.Where(p => p.Sku == sku).Select(p => p.Stock).SingleAsync();
    }
}
```

`tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Data;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class PriceCartJunctionTests
{
    private ShopfrontDbContext _db = null!;

    [SetUp]
    public async Task ResetTheDatabase()
    {
        await TestDatabase.Reset();
        _db = TestDatabase.NewContext();
    }

    [TearDown]
    public ValueTask DisposeTheContext() => _db.DisposeAsync();

    private PriceCartJunction Junction() =>
        new(new Catalogue(_db), NullLogger<PriceCartJunction>.Instance);

    private static PlaceOrderInput Order(params OrderLine[] lines) =>
        new()
        {
            CustomerId = "ada",
            Lines = [.. lines],
            PaymentToken = "tok_visa",
        };

    [Test]
    public async Task Run_TwoLines_PricesEachAndTotalsThem()
    {
        var cart = await Junction().Run(Order(new OrderLine("MUG", 2), new OrderLine("TEE", 1)));

        cart.Lines.Select(l => l.UnitPrice).Should().Equal(12.00m, 25.00m);
        cart.Total.Should().Be(49.00m);
    }

    [Test]
    public async Task Run_EmptyCart_Throws()
    {
        var act = () => Junction().Run(Order());

        await act.Should().ThrowAsync<TrainException>().WithMessage("The cart is empty.");
    }

    [Test]
    public async Task Run_UnknownSku_Throws()
    {
        var act = () => Junction().Run(Order(new OrderLine("HAT", 1)));

        await act.Should().ThrowAsync<TrainException>().WithMessage("HAT is not in the catalogue.");
    }

    [Test]
    public async Task Run_ZeroQuantity_Throws()
    {
        var act = () => Junction().Run(Order(new OrderLine("MUG", 0)));

        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("MUG: the quantity must be at least 1.");
    }
}
```

The stock tests read the stock back through a context of their own, which sees what was committed
rather than what one context remembers.

`tests/Shopfront.Tests/UnitTests/ReserveStockJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ReserveStockJunctionTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Data;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ReserveStockJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    private static ReserveStockJunction Junction(ShopfrontDbContext db) =>
        new(new Catalogue(db), NullLogger<ReserveStockJunction>.Instance);

    private static PricedCart Cart(params PricedLine[] lines) =>
        new([.. lines], lines.Sum(l => l.UnitPrice * l.Quantity));

    [Test]
    public async Task Run_InStock_ReservesEveryLine()
    {
        await using var db = TestDatabase.NewContext();

        await Junction(db).Run(Cart(new PricedLine("TEE", 3, 25.00m)));

        (await TestDatabase.StockOf("TEE")).Should().Be(0);
    }

    [Test]
    public async Task Run_OneLineOutOfStock_ThrowsAndReservesNothing()
    {
        await using var db = TestDatabase.NewContext();

        var act = () =>
            Junction(db)
                .Run(Cart(new PricedLine("MUG", 1, 12.00m), new PricedLine("CAP", 1, 18.50m)));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        (await TestDatabase.StockOf("MUG")).Should().Be(10, "the mug was not reserved either");
    }

    [Test]
    public async Task Run_StockChangedSinceItWasRead_IsRefused()
    {
        await using var first = TestDatabase.NewContext();
        await using var second = TestDatabase.NewContext();
        // Both orders read the three T-shirts before either saves.
        await first.Products.SingleAsync(p => p.Sku == "TEE");
        await second.Products.SingleAsync(p => p.Sku == "TEE");

        await Junction(first).Run(Cart(new PricedLine("TEE", 3, 25.00m)));
        var act = () => Junction(second).Run(Cart(new PricedLine("TEE", 3, 25.00m)));

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await TestDatabase.StockOf("TEE")).Should().Be(0, "the T-shirts were sold once");
    }
}
```

The third test is the race from the top of the chapter, played out in order: two contexts read the
same row, the first saves, and the second is refused.

`tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class TakePaymentJunctionTests
{
    private sealed class ScriptedPaymentProvider(Exception? failWith = null) : IPaymentProvider
    {
        public List<(string CustomerId, string PaymentToken, decimal Amount)> Charges { get; } = [];

        public Task<ChargeResult> Charge(
            string customerId,
            string paymentToken,
            decimal amount,
            CancellationToken cancellationToken
        )
        {
            Charges.Add((customerId, paymentToken, amount));
            return failWith is null
                ? Task.FromResult(new ChargeResult("tx_test", "https://pay.example/receipts/tx_test"))
                : Task.FromException<ChargeResult>(failWith);
        }
    }

    private static readonly PlaceOrderInput Order = new()
    {
        CustomerId = "ada",
        Lines = [new OrderLine("TEE", 3)],
        PaymentToken = "tok_visa",
    };

    private static readonly PricedCart Cart = new([new PricedLine("TEE", 3, 25.00m)], 75.00m);

    // Resets the database and reserves the three T-shirts, as ReserveStockJunction leaves them.
    [SetUp]
    public async Task ReserveTheTShirts()
    {
        await TestDatabase.Reset();
        await using var db = TestDatabase.NewContext();
        (await new Catalogue(db).TryReserve([("TEE", 3)], CancellationToken.None))
            .Should()
            .BeNull();
    }

    private static async Task<Payment> Run(IPaymentProvider payments)
    {
        await using var db = TestDatabase.NewContext();
        var junction = new TakePaymentJunction(
            payments,
            new Catalogue(db),
            NullLogger<TakePaymentJunction>.Instance
        );
        return await junction.Run((Order, Cart, new StockReservation(Guid.NewGuid())));
    }

    [Test]
    public async Task Run_Charged_ReturnsThePaymentWithItsReceipt()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Run(payments);

        payments.Charges.Should().Equal(("ada", "tok_visa", 75.00m));
        payment
            .Should()
            .Be(new Payment("tx_test", 75.00m, "https://pay.example/receipts/tx_test"));
        (await TestDatabase.StockOf("TEE")).Should().Be(0);
    }

    [Test]
    public async Task Run_Declined_ThrowsTheReasonAndReleasesTheStock()
    {
        var act = () =>
            Run(new ScriptedPaymentProvider(new PaymentDeclinedException("The card was declined.")));

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        (await TestDatabase.StockOf("TEE")).Should().Be(3, "the three T-shirts are back");
    }

    [Test]
    public async Task Run_TimedOut_RethrowsAndReleasesTheStock()
    {
        var act = () => Run(new ScriptedPaymentProvider(new TimeoutException("No answer.")));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        (await TestDatabase.StockOf("TEE")).Should().Be(3, "the three T-shirts are back");
    }
}
```

`tests/Shopfront.Tests/UnitTests/ConfirmOrderJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ConfirmOrderJunctionTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ConfirmOrderJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    [Test]
    public async Task Run_Paid_StoresTheOrderWithItsPrices()
    {
        var input = new PlaceOrderInput
        {
            CustomerId = "ada",
            Lines = [new OrderLine("MUG", 2)],
            PaymentToken = "tok_visa",
        };
        var cart = new PricedCart([new PricedLine("MUG", 2, 12.00m)], 24.00m);
        var payment = new Payment("tx_test", 24.00m, "https://pay.example/receipts/tx_test");

        await using (var db = TestDatabase.NewContext())
        {
            var output = await new ConfirmOrderJunction(
                db,
                NullLogger<ConfirmOrderJunction>.Instance
            ).Run((input, cart, payment));

            output.ReceiptUrl.Should().Be(payment.ReceiptUrl);
        }

        await using var read = TestDatabase.NewContext();
        var order = await read.Orders.Include(o => o.Items).SingleAsync();
        order.CustomerId.Should().Be("ada");
        order.TransactionId.Should().Be("tx_test");
        order.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(12.00m);
    }
}
```

`tests/Shopfront.Tests/UnitTests/ShopfrontFailureClassifierTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ShopfrontFailureClassifierTests.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Failures;
using Shopfront.Payments;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ShopfrontFailureClassifierTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(new TrainException("CAP is out of stock."), FailureClass.Permanent);
        yield return new TestCaseData(new TimeoutException(), FailureClass.Transient);
        yield return new TestCaseData(new DbUpdateConcurrencyException(), FailureClass.Transient);
        yield return new TestCaseData(new PaymentDeclinedException("Declined."), null);
        yield return new TestCaseData(new InvalidOperationException(), null);
    }

    [TestCaseSource(nameof(Cases))]
    public void Classify_Exception_GivesItsClass(Exception exception, FailureClass? expected) =>
        new ShopfrontFailureClassifier().Classify(exception).Should().Be(expected);
}
```

The train test registers the context against the test database and checks that a confirmed order
is stored.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.FailureClassifier;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class PlaceOrderTrainTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task BuildTheContainer()
    {
        await TestDatabase.Reset();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDomainDataContext<IShopfrontDbContext, ShopfrontDbContext>(options =>
            options.UseNpgsql(TestDatabase.ConnectionString)
        );
        services.AddScoped<Catalogue>();
        services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
        services.AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    private static PlaceOrderInput Order(string customerId, string paymentToken, string sku) =>
        new()
        {
            CustomerId = customerId,
            Lines = [new OrderLine(sku, 1)],
            PaymentToken = paymentToken,
        };

    [Test]
    public async Task RunAsync_InStock_StoresTheOrderAndMasksTheReceipt()
    {
        // A customer no other test uses, to find this run's record among any others.
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId).And.NotContain(output.ReceiptUrl);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.SingleAsync()).Id.Should().Be(output.OrderId);
        (await TestDatabase.StockOf("MUG")).Should().Be(9);
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAsPermanent()
    {
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "CAP"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureJunction.Should().Be("ReserveStockJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task RunAsync_Declined_FailsAtTakePaymentAndStoresNoOrder()
    {
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunAsync<PlaceOrderOutput>(
                Order(customerId, FakePaymentProvider.DeclinedToken, "MUG")
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        var run = RunFor(customerId);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.AnyAsync()).Should().BeFalse();
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_FifthCharge_TimesOutAsTransient()
    {
        var bus = _provider.GetRequiredService<ITrainBus>();
        for (var i = 0; i < 4; i++)
            await bus.RunAsync<PlaceOrderOutput>(Order(Guid.NewGuid().ToString(), "tok_visa", "MUG"));
        var customerId = Guid.NewGuid().ToString();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        var run = RunFor(customerId);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Transient);
    }

    private Metadata RunFor(string customerId)
    {
        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return data
            .Metadatas.Where(m => m.Name == typeof(IPlaceOrderTrain).FullName)
            .AsEnumerable()
            .Single(m => m.Input?.Contains(customerId) == true);
    }
}
```

The host tests point the application at the test database by overriding its connection string.

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Trains.PlaceOrder;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class HostTests
{
    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public async Task StartTheApplication()
    {
        await TestDatabase.Reset();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    private static object Order(string sku, string paymentToken = "tok_visa") =>
        new
        {
            customerId = "ada",
            paymentToken,
            lines = new[] { new { sku, quantity = 1 } },
        };

    [Test]
    public async Task PostOrders_InStock_Answers200WithTheOrder()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<PlaceOrderOutput>();
        order!.Total.Should().Be(12.00m);
    }

    [Test]
    public async Task PostOrders_OutOfStock_Answers422WithTheReason()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/orders", Order("CAP"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("CAP is out of stock.");
    }

    [Test]
    public async Task PostOrders_Declined_Answers422WithTheReason()
    {
        var response = await _app.CreateClient()
            .PostAsJsonAsync("/orders", Order("MUG", "tok_chargeDeclined"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("The card was declined.");
    }
}
```

The data-layer guards hold the context to the rules this chapter followed: it derives
`DomainDataContext`, it has its companion interface in a file of its own, and its schema is its
alone.

`tests/Shopfront.Tests/ArchitectureGuards.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/ArchitectureGuards.cs
using System.Reflection;
using Shopfront.Data;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Testing;
using Trax.Core.Testing.Fixtures;
using Trax.Effect.Data.Testing;
using Trax.Mediator.Testing;

namespace Shopfront.Tests;

[TestFixture]
public sealed class ShopfrontTrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies =>
        [typeof(IPlaceOrderTrain).Assembly];
}

[TestFixture]
public sealed class ShopfrontHygieneGuards : HygieneGuardFixture;

[TestFixture]
public sealed class ShopfrontDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["Data"] };

    protected override IReadOnlyList<Type> DomainContexts => [typeof(ShopfrontDbContext)];
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    34, Skipped:     0, Total:    34, Duration: 15 s - Shopfront.Tests.dll (net10.0)
```

Dropping and creating the database for each test is what makes these slower than the ones in
chapter 2, and what makes each one start from the same shelves.

## What we have

Shopfront has its own schema beside Trax's. Products, their stock and every confirmed order live in
it, a lost race for the last item is refused rather than sold twice, and the tests run against a
real database of their own.

Every order that is in stock and paid for goes through. The next chapter stops one that looks like
fraud before it is charged, with a rule the train asks rather than an `if` inside a junction.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)
