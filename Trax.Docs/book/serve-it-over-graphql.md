---
title: "7. Serve it over GraphQL"
description: "Chapter 7 of the Trax book: serve the catalogue, PlaceOrder and each customer's orders over GraphQL, and let a customer follow the run of their order."
type: tutorial
area: general
parent: The Trax book
nav_order: 17
snippet_context: none
follows: book/customers-and-staff
---

# 7. Serve it over GraphQL

Shopfront's API is two hand-written endpoints, and every new thing a client needs would be another.
In this chapter Trax generates the API instead: a GraphQL schema with the catalogue for everyone,
`PlaceOrder` for customers and each customer's own orders, from the trains and entities that are
already there and the rules already on them. A customer can also follow the run of their order,
junction by junction.

This chapter starts from the end of [chapter 6](/docs/book/customers-and-staff).

## Packages

`Trax.Api.GraphQL` serves the schema. It brings `Trax.Api` with it, so it replaces that reference.

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Api.Auth.Jwt" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.GraphQL" Version="1.61.0" />
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
    <PackageReference Include="Trax.Api.Auth.Jwt" />
    <PackageReference Include="Trax.Api.GraphQL" />
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.Postgres" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>
</Project>
```

## PlaceOrder becomes a mutation

`[TraxMutation]` puts a train in the schema as a mutation. `GraphQLOperation.Run` says the
mutation runs the train and answers with its output, as `POST /orders` did; the other operation,
queueing, comes with the worker later in the book. `[TraxRunProgress]` lets whoever started a run
follow it.

`Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderTrain.cs
using Shopfront.Auth;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

[TraxMutation(GraphQLOperation.Run)]
[TraxAuthorize(Roles = Roles.Customer)]
[TraxRunProgress]
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

The field's name comes from the interface: `IPlaceOrderTrain` without its `I` and `Train` is
`placeOrder`. Its argument is the train's input, `PlaceOrderInput`, which has no customer in it,
so the schema has none either: there is nothing a caller could send to order as someone else.

## The catalogue and the orders become queries

An entity marked `[TraxQueryModel]` is served as a query with paging, filtering and sorting. Like a
train, it must say who may read it, or the host does not start.

The catalogue is public on purpose. Its type has a member a shopper has no use for, the row version
from chapter 4, so the query serves an interface that lists what a listing shows.

`Data/Models/IProductListing.cs`:

```csharp compile=app file=Data/Models/IProductListing.cs
namespace Shopfront.Data.Models;

// What the catalogue shows anyone who asks.
public interface IProductListing
{
    string Sku { get; }
    string Name { get; }
    decimal Price { get; }
    int Stock { get; }
}
```

`Data/Models/Product.cs`:

```csharp compile=app file=Data/Models/Product.cs
using System.ComponentModel.DataAnnotations;
using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAllowAnonymous]
[TraxQueryModel(ExposeAs = typeof(IProductListing))]
public class Product : IProductListing
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

The orders need a signed-in caller, and the query filter from chapter 6 decides which rows that
caller gets: the query model reads through the same context, so a customer's query returns their
orders and a member of staff's returns all of them. The attribute is a bare `[TraxAuthorize]`, any
signed-in caller, because the rows, not the role, are the rule.

`Data/Models/Order.cs`:

```csharp compile=app file=Data/Models/Order.cs
using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAuthorize]
[TraxQueryModel]
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

An order's items are reached through the order, and anything the schema reaches needs a posture
too, even when it is not a query of its own.

`Data/Models/OrderItem.cs`:

```csharp compile=app file=Data/Models/OrderItem.cs
using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAuthorize]
public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

## Serve the schema

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;

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
                .AddJunctionEvents()
        )
        .AddMediator(typeof(Program).Assembly)
);
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<ShopfrontDbContext>());

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();

if (app.Environment.IsDevelopment())
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

await app.RunAsync();
```

The two hand-written endpoints are gone. What changed:

- **`AddTraxGraphQL`** builds the schema from every train with a GraphQL attribute and every query
  model on the contexts it is given. It comes after `AddTrax`, because the trains have to be
  registered before the schema can be built from them. It also registers what `AddTraxApi` did,
  including the service that checks `[TraxAuthorize]`, which every field of the schema goes through.
- **`AddDbContext<ShopfrontDbContext>()`** names the context the query models read from. It resolves
  the context per request, built for that request's caller, so the query filter applies.
- **`AddJunctionEvents()`** records each junction of a run as a step, which is what a customer
  following a run sees.
- **`UseTraxGraphQL()`** serves the schema at `/trax/graphql`.

## Ask it

```bash
dotnet run --urls http://localhost:5080
```

The catalogue needs nobody signed in:

```bash
curl -s http://localhost:5080/trax/graphql -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { products(order: { sku: ASC }) { nodes { sku name price stock } } } }"}'
```

```json
{"data":{"discover":{"products":{"nodes":[{"sku":"CAP","name":"Cap","price":18.50,"stock":0},{"sku":"MUG","name":"Mug","price":12.00,"stock":10},{"sku":"TEE","name":"T-shirt","price":25.00,"stock":3}]}}}}
```

Your stock is lower, after the orders of the earlier chapters. The row version is not a field: the
schema serves `IProductListing`, not `Product`.

Place an order as `ada`. The answer carries the run's `externalId`, which is how a run is named to
everything that can follow it:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $ADA" \
  -H 'Content-Type: application/json' -d '{
  "query": "mutation Place($input: PlaceOrderInput!) { dispatch { placeOrder(input: $input) { externalId output { orderId total } } } }",
  "variables": { "input": { "paymentToken": "tok_visa", "lines": [{ "sku": "MUG", "quantity": 1 }] } }
}'
```

```json
{"data":{"dispatch":{"placeOrder":{"externalId":"f3e0f595ed6a472b926c46dbaf671e8d","output":{"orderId":"c5625240-1274-4e08-b2ff-2115a19fc9aa","total":12.00}}}}}
```

Read the run back, step by step:

```bash
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $ADA" \
  -H 'Content-Type: application/json' -d '{
  "query": "query Progress($runId: String!) { runProgress(runId: $runId) { state steps { position name state } problem { code } } }",
  "variables": { "runId": "f3e0f595ed6a472b926c46dbaf671e8d" }
}'
```

```json
{"data":{"runProgress":{"state":"COMPLETED","steps":[{"position":0,"name":"IdentifyCustomerJunction","state":"COMPLETED"},{"position":1,"name":"PriceCartJunction","state":"COMPLETED"},{"position":2,"name":"AssessRiskJunction","state":"COMPLETED"},{"position":3,"name":"LooksFraudulent","state":"COMPLETED"},{"position":4,"name":"LooksFraudulent","state":"COMPLETED"},{"position":5,"name":"(withheld)","state":"COMPLETED"},{"position":6,"name":"(withheld)","state":"COMPLETED"},{"position":7,"name":"(withheld)","state":"COMPLETED"}],"problem":null}}}
```

Use your own `externalId`. The first three steps are the junctions before the fraud check. Then
the question is asked and routed on, as two steps. After that the names are withheld, and that is
deliberate: which junctions ran after a decision says what was decided, and a customer is not told
how the fraud check judged them. `[TraxRunProgress(Answers = true)]` would show them, and the
answer with them, for a train whose decisions its caller may see.

A run is followed by the caller who started it and by nobody else. Ask the same as `grace` and the
answer is `NOT_FOUND`, the same answer as for a run that does not exist, so it tells her nothing
about `ada`'s orders:

```json
{"data":{"runProgress":{"state":null,"steps":[],"problem":{"code":"NOT_FOUND"}}}}
```

A run started over a `mutation` returns once it has finished, so here the steps are all done. A
subscription to the same `runProgress` streams each step as it happens, which matters once runs
are queued and take longer than a request.

Errors come back as GraphQL errors. A plain `TrainException` keeps its message for the customer,
under the code `TRAX_TRAIN_ERROR`:

```json
{"errors":[{"message":"The card was declined.","path":["dispatch","placeOrder"],"extensions":{"code":"TRAX_TRAIN_ERROR"}}],"data":{"dispatch":{"placeOrder":null}}}
```

Anything else is masked, so the payment provider's timeout, with our provider's name in its
message, reaches the customer as:

```json
{"errors":[{"message":"Unexpected Execution Error","path":["dispatch","placeOrder"]}],"data":{"dispatch":{"placeOrder":null}}}
```

This is why chapter 1 threw `TrainException` for every message meant for the customer and nothing
else.

## Test it

The host tests speak GraphQL now. A small helper posts a query and returns the response.

`tests/Shopfront.Tests/IntegrationTests/GraphQL.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/GraphQL.cs
using System.Net.Http.Json;
using System.Text.Json;

namespace Shopfront.Tests.IntegrationTests;

internal static class GraphQL
{
    public static async Task<JsonElement> Post(
        this HttpClient client,
        string query,
        object? variables = null
    )
    {
        var response = await client.PostAsJsonAsync("/trax/graphql", new { query, variables });
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    // The extensions.code of each error, empty when there are none.
    public static IEnumerable<string?> ErrorCodes(this JsonElement response) =>
        response.TryGetProperty("errors", out var errors)
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var x) && x.TryGetProperty("code", out var c)
                        ? c.GetString()
                        : null
                )
            : [];
}
```

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class HostTests
{
    private const string PlaceOrder = """
        mutation Place($input: PlaceOrderInput!) {
          dispatch { placeOrder(input: $input) { externalId output { orderId total } } }
        }
        """;

    private const string Orders = "{ discover { orders { nodes { customerId total } } } }";

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
        new
        {
            input = new { paymentToken, lines = new[] { new { sku, quantity = 1 } } },
        };

    private static JsonElement Placed(JsonElement response) =>
        response.GetProperty("data").GetProperty("dispatch").GetProperty("placeOrder");

    [Test]
    public async Task Products_Anonymous_ListsTheCatalogue()
    {
        var response = await _app.CreateClient()
            .Post("{ discover { products(order: { sku: ASC }) { nodes { sku } } } }");

        response
            .GetProperty("data")
            .GetProperty("discover")
            .GetProperty("products")
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(p => p.GetProperty("sku").GetString())
            .Should()
            .Equal("CAP", "MUG", "TEE");
    }

    [Test]
    public async Task PlaceOrder_Anonymous_IsNotAuthorized()
    {
        var response = await _app.CreateClient().Post(PlaceOrder, Order("MUG"));

        response.ErrorCodes().Should().Contain("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task PlaceOrder_Staff_IsNotAuthorized()
    {
        var response = await SignedIn("lin", Roles.Staff).Post(PlaceOrder, Order("MUG"));

        response.ErrorCodes().Should().Contain("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task PlaceOrder_Customer_AnswersTheOrder()
    {
        var response = await SignedIn("ada", Roles.Customer).Post(PlaceOrder, Order("MUG"));

        Placed(response).GetProperty("output").GetProperty("total").GetDecimal().Should().Be(12.00m);
    }

    [Test]
    public async Task PlaceOrder_Declined_AnswersTheReason()
    {
        var response = await SignedIn("ada", Roles.Customer)
            .Post(PlaceOrder, Order("MUG", "tok_chargeDeclined"));

        response.ErrorCodes().Should().Contain("TRAX_TRAIN_ERROR");
        response
            .GetProperty("errors")[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("The card was declined.");
    }

    [Test]
    public async Task Orders_EachCustomerSeesTheirOwn_StaffSeeAll()
    {
        await SignedIn("ada", Roles.Customer).Post(PlaceOrder, Order("MUG"));
        await SignedIn("grace", Roles.Customer).Post(PlaceOrder, Order("MUG"));

        static IEnumerable<string?> Owners(JsonElement response) =>
            response
                .GetProperty("data")
                .GetProperty("discover")
                .GetProperty("orders")
                .GetProperty("nodes")
                .EnumerateArray()
                .Select(o => o.GetProperty("customerId").GetString());

        Owners(await SignedIn("grace", Roles.Customer).Post(Orders))
            .Should()
            .Equal("TraxJwt:grace");
        Owners(await SignedIn("lin", Roles.Staff).Post(Orders))
            .Should()
            .BeEquivalentTo(["TraxJwt:ada", "TraxJwt:grace"]);
    }

    [Test]
    public async Task RunProgress_OnlyTheStarter_CanFollowTheRun()
    {
        const string progress = """
            query Progress($runId: String!) {
              runProgress(runId: $runId) { state steps { name } problem { code } }
            }
            """;
        var placed = Placed(await SignedIn("ada", Roles.Customer).Post(PlaceOrder, Order("MUG")));
        var runId = new { runId = placed.GetProperty("externalId").GetString() };

        var mine = (await SignedIn("ada", Roles.Customer).Post(progress, runId))
            .GetProperty("data")
            .GetProperty("runProgress");
        var hers = (await SignedIn("grace", Roles.Customer).Post(progress, runId))
            .GetProperty("data")
            .GetProperty("runProgress");

        mine.GetProperty("state").GetString().Should().Be("COMPLETED");
        mine.GetProperty("steps")
            .EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .Should()
            .StartWith("IdentifyCustomerJunction");
        hers.GetProperty("problem").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 27 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront serves a GraphQL schema that Trax built from what was already there: the catalogue for
anyone, `PlaceOrder` for customers, each customer's own orders, and the progress of a run for the
one customer who started it. Not a line of it repeats a rule: the roles are on the train, the rows
on the context, and the schema follows both.

It also answers any query anyone writes against it, including ones no client of ours will ever
send. The next chapter closes that: Shopfront will run only the operations it already knows.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [Query models](/docs/sdk-reference/graphql-api/query-models) | [Run progress](/docs/sdk-reference/graphql-api/run-progress) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch)
