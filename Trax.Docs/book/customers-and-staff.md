---
title: "6. Customers and staff"
description: "Chapter 6 of the Trax book: sign customers and staff in, guard PlaceOrder with TraxAuthorize, and scope each order to the customer who placed it."
type: tutorial
area: general
parent: The Trax book
nav_order: 16
snippet_context: none
follows: book/does-this-order-look-like-fraud
---

# 6. Customers and staff

Until now anyone could call `POST /orders`, and say they were anyone: the customer was a field in
the request. In this chapter Shopfront learns who is asking. A customer signs in and places orders
as themselves, reads their own orders and nobody else's, and a member of staff reads every order
but cannot place one.

This chapter starts from the end of [chapter 5](/docs/book/does-this-order-look-like-fraud).

## Fail closed

Trax treats every gap in authorization as a reason not to start. A train that declares who may run
it with no service to check it, a junction that asks for the caller where no caller can be
resolved, a demonstration signing key outside Development: each stops the host before it takes a
request, with a message that says which line to write. This chapter meets several of them on
purpose.

## Packages

`Trax.Api.Auth.Jwt` signs callers in with JSON Web Tokens and turns each one into a Trax
principal. `Trax.Api` checks `[TraxAuthorize]` against that principal.

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Api" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.Auth.Jwt" Version="1.61.0" />
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
    <PackageReference Include="Trax.Api" />
    <PackageReference Include="Trax.Api.Auth.Jwt" />
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.Postgres" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>
</Project>
```

## Roles and a development signing key

Two roles cover Shopfront's callers. Role names are compared exactly, so they are written once, as
constants.

`Auth/Roles.cs`:

```csharp compile=app file=Auth/Roles.cs
namespace Shopfront.Auth;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";
}
```

In production the tokens come from an identity provider. On a developer's machine there is none, so
Shopfront signs its own, with a key that says what it is. Trax refuses to start outside Development
with any key containing `do-not-use-in-production`, so this one can be committed and can never
guard a real deployment.

`Auth/DevJwt.cs`:

```csharp compile=app file=Auth/DevJwt.cs
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Shopfront.Auth;

// Tokens for Development only. The host refuses this key in any other environment.
public static class DevJwt
{
    public const string Issuer = "shopfront-dev";
    public const string Audience = "shopfront";

    public static readonly byte[] Key = Encoding.UTF8.GetBytes(
        "shopfront-dev-signing-key-do-not-use-in-production"
    );

    public static string Mint(string user, string role) =>
        new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = Audience,
                Subject = new ClaimsIdentity(
                    [new Claim("sub", user), new Claim("name", user), new Claim("role", role)]
                ),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Key),
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
}
```

Trax reads the token's `sub` as the caller's id, `name` as their display name and `role` as their
roles. It qualifies the id with the scheme that signed the caller in, so `ada` signed in by JWT is
`TraxJwt:ada`. Two schemes that both have an `ada` can then never be mistaken for one person.

## Who may run PlaceOrder

`[TraxAuthorize]` on a train says who may run it.

`Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderTrain.cs
using Shopfront.Auth;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

[TraxAuthorize(Roles = Roles.Customer)]
public class PlaceOrderTrain : ServiceTrain<PlaceOrderInput, PlaceOrderOutput>, IPlaceOrderTrain
{
    protected override Task<Either<Exception, PlaceOrderOutput>> Junctions() =>
        Chain<IdentifyCustomerJunction>()
            .Chain<PriceCartJunction>()
            .Chain<AssessRiskJunction>()
            .Gate<OrderRisk, LooksFraudulent>(gate =>
                gate.Yes(hold => hold.Chain<RefuseOrderJunction>())
                    .No(take => take)
                    .Shadow<ICandidateFraudDecider>()
                    .WaitForShadows(TimeSpan.FromSeconds(1))
            )
            .Chain<ReserveStockJunction>()
            .Chain<TakePaymentJunction>()
            .Chain<ConfirmOrderJunction>()
            .Resolve();
}
```

The attribute is checked where a train is run on someone's behalf: through
`ITrainExecutionService`, which the GraphQL API of the next chapter also uses. It is not checked
when code that is already trusted runs a train directly, through its interface or the train bus,
as the tests do and as the scheduler will. [Where authorization is
checked](/docs/security/where-authorization-is-checked) has the full map.

## The customer is the caller

The customer used to be a field of the input, which let anyone order as anyone. It comes from the
caller now, and the input carries only what the caller chooses: the lines and the payment token.

`Trains/PlaceOrder/PlaceOrderInput.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderInput.cs
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderInput
{
    public required List<OrderLine> Lines { get; init; }
    public required string PaymentToken { get; init; }
}
```

One junction asks who the caller is and puts the answer in Memory, and every later junction reads
it from there. Only this junction depends on how a caller is known, so only it changes if that
does.

`Trains/PlaceOrder/Models/Customer.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/Customer.cs
namespace Shopfront.Trains.PlaceOrder.Models;

internal record Customer(string Id);
```

`Trains/PlaceOrder/Junctions/IdentifyCustomerJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/IdentifyCustomerJunction.cs
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Api.Auth;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class IdentifyCustomerJunction(TraxPrincipal principal)
    : EffectJunction<PlaceOrderInput, Customer>
{
    public override Task<Customer> Run(PlaceOrderInput input) =>
        Task.FromResult(new Customer(principal.Id));
}
```

`TraxPrincipal` is the signed-in caller: their qualified id, display name and roles. Resolving it
when nobody is signed in throws, so a run with no caller fails at this junction rather than
placing an order for nobody.

The junctions that used the input's customer take the `Customer` from Memory instead.

`Trains/PlaceOrder/Junctions/AssessRiskJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/AssessRiskJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class AssessRiskJunction(IShopfrontDbContext db)
    : EffectJunction<(Customer Customer, PricedCart Cart), OrderRisk>
{
    public override async Task<OrderRisk> Run((Customer Customer, PricedCart Cart) input)
    {
        var ordered = await db.Orders.AnyAsync(
            o => o.CustomerId == input.Customer.Id,
            CancellationToken
        );
        return new OrderRisk(input.Customer.Id, input.Cart.Total, FirstOrder: !ordered);
    }
}
```

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
)
    : EffectJunction<
        (Customer Customer, PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation),
        Payment
    >
{
    public override async Task<Payment> Run(
        (Customer Customer, PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation) input
    )
    {
        ChargeResult charge;
        try
        {
            charge = await payments.Charge(
                input.Customer.Id,
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

`Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Data;
using Shopfront.Data.Models;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(IShopfrontDbContext db, ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(Customer Customer, PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override async Task<PlaceOrderOutput> Run(
        (Customer Customer, PricedCart Cart, Payment Payment) input
    )
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = input.Customer.Id,
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

## A customer reads their own orders

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

## Sign in, and run trains as the caller

`Program.cs`:

```csharp compile=app file=Program.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Trains.PlaceOrder;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Exceptions;
using Trax.Api.Extensions;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainExecution;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddShopfront(connectionString);

if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxJwtAuth(jwt =>
        jwt.UseSymmetricKey(DevJwt.Issuer, DevJwt.Audience, DevJwt.Key)
    );
else
    builder.Services.AddTraxJwtAuth(
        builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is not set."),
        builder.Configuration["Auth:Audience"]
            ?? throw new InvalidOperationException("Auth:Audience is not set.")
    );
builder.Services.AddAuthorization();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddDecisionRecording()
        )
        .AddMediator(typeof(Program).Assembly)
);
builder.Services.AddTraxApi();

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost(
        "/orders",
        async (PlaceOrderInput input, ITrainExecutionService trains, CancellationToken ct) =>
        {
            try
            {
                var run = await trains.RunAsync(
                    typeof(IPlaceOrderTrain).FullName!,
                    JsonSerializer.Serialize(input),
                    ct
                );
                return Results.Ok(run.Output);
            }
            catch (TrainAuthorizationException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (TrainException ex)
            {
                return Results.UnprocessableEntity(ex.Message);
            }
        }
    )
    .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);

app.MapGet(
        "/orders",
        async (IShopfrontDbContext db, CancellationToken ct) =>
            await db
                .Orders.OrderBy(o => o.PlacedAt)
                .Select(o => new
                {
                    o.Id,
                    o.CustomerId,
                    o.Total,
                    o.PlacedAt,
                })
                .ToListAsync(ct)
    )
    .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);

if (app.Environment.IsDevelopment())
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

await app.RunAsync();
```

From the top:

- **`AddTraxJwtAuth`** registers the JWT scheme as `TraxJwt`, and the services that turn a signed-in
  caller into `TraxPrincipal` and `TraxCaller`. In Development it checks tokens against the
  development key; anywhere else against an identity provider, read from configuration. Nothing
  else changes between the two, because the rest of the application sees only the principal.
- **`AddTraxApi`** registers the service that checks `[TraxAuthorize]`. Leave it out and the host
  does not start; that is below.
- **`RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy)`** on each endpoint signs the caller
  in with Trax's schemes and refuses anyone who is not, with a `401`. The Trax scheme is never the
  application's default, so an endpoint that says nothing signs nobody in.
- **`POST /orders`** runs `PlaceOrder` through `ITrainExecutionService`, by its interface's name and
  with its input as JSON. That is the path that checks `[TraxAuthorize]`: a member of staff is
  signed in, so passes the endpoint, and is refused by the train with a `403`.
- **`GET /orders`** queries the orders. It has no `Where`: the query filter is the rule.
- **`/dev/token/{user}`** mints a token, in Development only. Outside it, the route does not exist.

## Try it

```bash
dotnet run --urls http://localhost:5080
```

Ordering without signing in is refused:

```bash
curl -i -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":1}]}'
```

```text
HTTP/1.1 401 Unauthorized
```

Sign in as `ada` and order a mug:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
curl -X POST http://localhost:5080/orders -H "Authorization: Bearer $ADA" \
  -H 'Content-Type: application/json' \
  -d '{"paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":1}]}'
```

Sign in as `grace` and do the same. Then list the orders as each:

```bash
GRACE=$(curl -s http://localhost:5080/dev/token/grace)
curl -s http://localhost:5080/orders -H "Authorization: Bearer $ADA"
curl -s http://localhost:5080/orders -H "Authorization: Bearer $GRACE"
```

```json
[{"id":"3179f9c4-07ba-4f64-ac64-e7026995cc89","customerId":"TraxJwt:ada","total":12.00,"placedAt":"2026-10-18T19:30:06.689317Z"}]
[{"id":"fb90a6f5-f5e0-4a89-b6cc-d76141452907","customerId":"TraxJwt:grace","total":12.00,"placedAt":"2026-10-18T19:30:06.837553Z"}]
```

Each sees their own. A member of staff sees both, and every order from the chapters before this
one, but cannot place an order:

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s http://localhost:5080/orders -H "Authorization: Bearer $LIN"
curl -i -X POST http://localhost:5080/orders -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":1}]}'
```

```text
HTTP/1.1 403 Forbidden
```

The orders from earlier chapters belong to `ada` and `grace` without a scheme in front of them,
so no customer who signs in now sees them. They are staff's to read.

## What it refuses

Comment out `builder.Services.AddTraxApi();` and start the application:

```text
fail: Microsoft.Extensions.Hosting.Internal.Host[11]
      Hosting failed to start
      System.InvalidOperationException: One or more registered trains declare [TraxAuthorize] but no ITrainAuthorizationService is registered in the DI container. Call services.AddTraxApi() (or register a custom ITrainAuthorizationService) before building the host. If this process intentionally runs no API submissions (for example a scheduler-only worker), opt out via AddMediator(m => m.AllowMissingAuthorizationService()). Authorized trains: IPlaceOrderTrain.
```

`PlaceOrder` declares who may run it, and nothing is registered to check it, so the host refuses
to serve rather than run every train for everyone.

Put the line back. Now start the application outside Development:

```bash
ASPNETCORE_ENVIRONMENT=Production Auth__Authority=https://id.example Auth__Audience=shopfront \
  dotnet run --urls http://localhost:5080
```

It starts, and `/dev/token/ada` answers `404`: Production reads its keys from the identity
provider, never sees the development key, and has no route that mints tokens. Had the development key been registered there, the host
would have refused it by its marker.

## Test it

The tests run `PlaceOrder` through the train bus, which is trusted and checks no attribute, so the
train tests give the run a caller themselves. The host tests sign in for real, with the development
key, and check what each caller can do.

Tests read the database as a member of staff unless they say otherwise, so they see every order.

`tests/Shopfront.Tests/TestCaller.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/TestCaller.cs
using Shopfront.Data;

namespace Shopfront.Tests;

internal sealed record TestCaller(string? CustomerId, bool IsStaff) : IShopfrontCaller
{
    public static readonly TestCaller Staff = new(null, IsStaff: true);

    public static TestCaller Customer(string id) => new(id, IsStaff: false);
}
```

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

    // A context for a caller; staff, who read every order, unless the test says otherwise.
    public static ShopfrontDbContext NewContext(IShopfrontCaller? caller = null) =>
        new(
            new DbContextOptionsBuilder<ShopfrontDbContext>().UseNpgsql(ConnectionString).Options,
            caller ?? TestCaller.Staff
        );

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

The junction tests change only where their inputs did.

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
        new() { Lines = [.. lines], PaymentToken = "tok_visa" };

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

`tests/Shopfront.Tests/UnitTests/AssessRiskJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/AssessRiskJunctionTests.cs
using Shopfront.Data.Models;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class AssessRiskJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    private static readonly PricedCart Cart = new([new PricedLine("MUG", 1, 12.00m)], 12.00m);

    [Test]
    public async Task Run_NoEarlierOrder_IsAFirstOrder()
    {
        await using var db = TestDatabase.NewContext();

        var risk = await new AssessRiskJunction(db).Run((new Customer("TraxJwt:ada"), Cart));

        risk.Should().Be(new OrderRisk("TraxJwt:ada", 12.00m, FirstOrder: true));
    }

    [Test]
    public async Task Run_AnEarlierOrder_IsNotAFirstOrder()
    {
        await using (var seed = TestDatabase.NewContext())
        {
            seed.Orders.Add(
                new Order
                {
                    CustomerId = "TraxJwt:ada",
                    TransactionId = "tx_earlier",
                    Total = 12.00m,
                }
            );
            await seed.SaveChangesAsync();
        }
        // Read as ada herself, through the filter, as a run of her order does.
        await using var db = TestDatabase.NewContext(TestCaller.Customer("TraxJwt:ada"));

        var risk = await new AssessRiskJunction(db).Run((new Customer("TraxJwt:ada"), Cart));

        risk.FirstOrder.Should().BeFalse();
    }
}
```

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

    private static readonly Customer Ada = new("TraxJwt:ada");

    private static readonly PlaceOrderInput Order = new()
    {
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
        return await junction.Run((Ada, Order, Cart, new StockReservation(Guid.NewGuid())));
    }

    [Test]
    public async Task Run_Charged_ChargesTheCallerAndReturnsTheReceipt()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Run(payments);

        payments.Charges.Should().Equal(("TraxJwt:ada", "tok_visa", 75.00m));
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
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ConfirmOrderJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    [Test]
    public async Task Run_Paid_StoresTheOrderForTheCaller()
    {
        var cart = new PricedCart([new PricedLine("MUG", 2, 12.00m)], 24.00m);
        var payment = new Payment("tx_test", 24.00m, "https://pay.example/receipts/tx_test");

        await using (var db = TestDatabase.NewContext())
        {
            var output = await new ConfirmOrderJunction(
                db,
                NullLogger<ConfirmOrderJunction>.Instance
            ).Run((new Customer("TraxJwt:ada"), cart, payment));

            output.ReceiptUrl.Should().Be(payment.ReceiptUrl);
        }

        await using var read = TestDatabase.NewContext();
        var order = await read.Orders.Include(o => o.Items).SingleAsync();
        order.CustomerId.Should().Be("TraxJwt:ada");
        order.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(12.00m);
    }
}
```

The filter gets tests of its own: the census below proves it exists, these prove it compares the
right column.

`tests/Shopfront.Tests/UnitTests/OrderFilterTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/OrderFilterTests.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Data.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class OrderFilterTests
{
    [SetUp]
    public async Task OneOrderEach()
    {
        await TestDatabase.Reset();
        await using var db = TestDatabase.NewContext();
        foreach (var customer in new[] { "TraxJwt:ada", "TraxJwt:grace" })
            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customer,
                    TransactionId = $"tx_{customer}",
                    Total = 12.00m,
                    Items =
                    [
                        new OrderItem
                        {
                            Sku = "MUG",
                            Quantity = 1,
                            UnitPrice = 12.00m,
                        },
                    ],
                }
            );
        await db.SaveChangesAsync();
    }

    private static async Task<List<string>> OwnersSeenBy(IShopfrontCaller caller)
    {
        await using var db = TestDatabase.NewContext(caller);
        return await db.Orders.Select(o => o.CustomerId).OrderBy(c => c).ToListAsync();
    }

    [Test]
    public async Task Orders_ACustomer_SeesOnlyTheirOwn() =>
        (await OwnersSeenBy(TestCaller.Customer("TraxJwt:ada"))).Should().Equal("TraxJwt:ada");

    [Test]
    public async Task Orders_Staff_SeeEveryOrder() =>
        (await OwnersSeenBy(TestCaller.Staff)).Should().Equal("TraxJwt:ada", "TraxJwt:grace");

    [Test]
    public async Task Orders_Nobody_SeesNothing() =>
        (await OwnersSeenBy(NoShopfrontCaller.Instance)).Should().BeEmpty();

    [Test]
    public async Task Items_ACustomer_SeesOnlyTheirOrdersItems()
    {
        await using var db = TestDatabase.NewContext(TestCaller.Customer("TraxJwt:ada"));

        (await db.Set<OrderItem>().CountAsync()).Should().Be(1);
    }
}
```

The train tests give each run a signed-in customer, and find its record by a payment token no
other test uses, now that the input has no customer in it.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Fraud;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Api.Auth;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class PlaceOrderTrainTests
{
    private const string Ada = "TraxJwt:ada";

    private ServiceProvider _provider = null!;

    private async Task Build(Action<IServiceCollection>? replace = null)
    {
        await TestDatabase.Reset();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShopfront(TestDatabase.ConnectionString);
        // The caller a signed-in request would have given the run.
        services.AddScoped(_ => new TraxPrincipal(Ada, "ada", [Roles.Customer]));
        services.AddScoped<IShopfrontCaller>(_ => TestCaller.Customer(Ada));
        replace?.Invoke(services);
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    // A payment token no other test uses, to find this run's record among any others.
    private static string NewToken() => $"tok_{Guid.NewGuid():N}";

    private static PlaceOrderInput Order(string paymentToken, string sku, int quantity = 1) =>
        new() { Lines = [new OrderLine(sku, quantity)], PaymentToken = paymentToken };

    [Test]
    public async Task RunAsync_InStock_StoresTheOrderForTheCaller()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG"));

        var run = RunFor(token);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId).And.NotContain(output.ReceiptUrl);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.SingleAsync()).CustomerId.Should().Be(Ada);
        (await TestDatabase.StockOf("MUG")).Should().Be(9);
    }

    [Test]
    public async Task RunAsync_LargeFirstOrder_IsHeldBeforeStockOrPayment()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG", 9));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*You have not been charged.");
        RunFor(token).FailureJunction.Should().Be("RefuseOrderJunction");
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_DeciderSaysYes_HoldsEvenASmallOrder()
    {
        var decider = new ScriptedDecider().YesNo<LooksFraudulent>(0.9);
        await Build(services => services.AddSingleton<IDecider>(decider));
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(NewToken(), "MUG"));

        await act.Should().ThrowAsync<TrainException>();
        decider
            .Requests.Should()
            .ContainSingle()
            .Which.State.Should()
            .Be(new OrderRisk(Ada, 12.00m, FirstOrder: true));
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAsPermanent()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "CAP"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        var run = RunFor(token);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureJunction.Should().Be("ReserveStockJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task RunAsync_Declined_FailsAtTakePaymentAndStoresNoOrder()
    {
        await Build();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunAsync<PlaceOrderOutput>(Order(FakePaymentProvider.DeclinedToken, "MUG"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        var run = RunFor(FakePaymentProvider.DeclinedToken);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.AnyAsync()).Should().BeFalse();
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_FifthCharge_TimesOutAsTransient()
    {
        await Build();
        var bus = _provider.GetRequiredService<ITrainBus>();
        for (var i = 0; i < 4; i++)
            await bus.RunAsync<PlaceOrderOutput>(Order(NewToken(), "MUG"));
        var token = NewToken();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG"));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        var run = RunFor(token);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Transient);
    }

    private Metadata RunFor(string paymentToken)
    {
        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return data
            .Metadatas.Where(m => m.Name == typeof(IPlaceOrderTrain).FullName)
            .AsEnumerable()
            .Single(m => m.Input?.Contains(paymentToken) == true);
    }
}
```

The host tests are where `[TraxAuthorize]` and the endpoints are checked, because that is where a
real caller signs in. They mint tokens with the development key, which the host accepts in
Development, the environment `WebApplicationFactory` starts it in here.

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;
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
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    private HttpClient SignedIn(string user, string role)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint(user, role)
        );
        return client;
    }

    private static object Order(string sku, string paymentToken = "tok_visa") =>
        new { paymentToken, lines = new[] { new { sku, quantity = 1 } } };

    private sealed record OrderRow(Guid Id, string CustomerId, decimal Total);

    [Test]
    public async Task PostOrders_NotSignedIn_Answers401()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PostOrders_Staff_Answers403()
    {
        var response = await SignedIn("lin", Roles.Staff).PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PostOrders_Customer_PlacesTheOrderAsThemselves()
    {
        var ada = SignedIn("ada", Roles.Customer);

        var response = await ada.PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<PlaceOrderOutput>();
        var mine = await ada.GetFromJsonAsync<List<OrderRow>>("/orders");
        mine.Should().ContainSingle().Which.Should().Be(new OrderRow(order!.OrderId, "TraxJwt:ada", 12.00m));
    }

    [Test]
    public async Task PostOrders_Declined_Answers422WithTheReason()
    {
        var response = await SignedIn("ada", Roles.Customer)
            .PostAsJsonAsync("/orders", Order("MUG", "tok_chargeDeclined"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("The card was declined.");
    }

    [Test]
    public async Task GetOrders_EachCustomerSeesTheirOwn_StaffSeeAll()
    {
        var ada = SignedIn("ada", Roles.Customer);
        var grace = SignedIn("grace", Roles.Customer);
        await ada.PostAsJsonAsync("/orders", Order("MUG"));
        await grace.PostAsJsonAsync("/orders", Order("MUG"));

        (await grace.GetFromJsonAsync<List<OrderRow>>("/orders"))!
            .Select(o => o.CustomerId)
            .Should()
            .Equal("TraxJwt:grace");
        (await SignedIn("lin", Roles.Staff).GetFromJsonAsync<List<OrderRow>>("/orders"))!
            .Select(o => o.CustomerId)
            .Should()
            .BeEquivalentTo(["TraxJwt:ada", "TraxJwt:grace"]);
    }
}
```

The last test is the one the owner-scope guard asks every application to write: one customer
creates a row, a second customer reads, and gets nothing of the first's.

The data-layer guard learns which rows belong to someone. Its owner-scope census reads the model
and fails on any entity that holds an owner's id, or hangs off one that does, without a filter that
reads the caller.

`tests/Shopfront.Tests/ArchitectureGuards.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/ArchitectureGuards.cs
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shopfront.Data;
using Shopfront.Data.Models;
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

    // The model, built with no database and no caller, for the census to read.
    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<ShopfrontDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .Options;
            using var context = new ShopfrontDbContext(options, NoShopfrontCaller.Instance);
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope =>
        new()
        {
            OwnerType = typeof(Order),
            PrincipalAccessorType = typeof(IShopfrontCaller),
            OwnerIdProperties = new HashSet<string> { nameof(Order.CustomerId) },
        };
}
```

`OwnerType` is the entity rows belong to. Shopfront has no customer table, so it is the order
itself, owned through `CustomerId`, and the census then also requires a filter on `OrderItem`,
which hangs off it. Delete the item filter and this guard fails, naming `OrderItem`.

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    49, Skipped:     0, Total:    49, Duration: 19 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront knows who is asking. A customer orders as themselves and reads only their own orders, a
member of staff reads every order and places none, and a caller nobody signed in is refused at the
door. The rules live where they hold everywhere: who may run a train on the train, which rows a
caller may read on the data. A host missing either check refuses to start.

The API is two hand-written endpoints. The next chapter replaces them with GraphQL: the catalogue
for everyone, `PlaceOrder` for customers, and each customer's orders, all from the same rules.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)
