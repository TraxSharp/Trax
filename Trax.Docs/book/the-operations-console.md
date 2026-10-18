---
title: "8. The operations console"
description: "Chapter 8 of the Trax book: open the operations console as a member of staff to read every run, its failure and its decisions, behind the operations gate."
type: tutorial
area: general
parent: The Trax book
nav_order: 18
snippet_context: none
follows: book/serve-it-over-graphql
---

# 8. The operations console

Every run of `PlaceOrder` is recorded with its input, output, failure and decisions, and so far the
only way to read any of it has been `psql`. In this chapter Shopfront serves the operations
console: a page where a member of staff reads every run, opens a failed one to see which junction
threw and why, and follows runs as they happen. Customers cannot reach any of it.

This chapter starts from the end of [chapter 7](/docs/book/serve-it-over-graphql).

## The console reads through GraphQL

The console is a single-page app Trax ships. It holds no data of its own: everything it shows or
changes is a GraphQL request to the `operations` namespace of Shopfront's schema, which is off
until a host turns it on and refuses to start turned on without a gate. That gate is what keeps
customers out, on every request, whatever page they load.

## Packages

`Trax.Api.Dashboard` carries the console. `Trax.Scheduler` provides the operations service the
namespace reads through. Nothing is scheduled yet; that starts in the next part.

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Api.Auth.Jwt" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.Dashboard" Version="1.61.0" />
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
    <PackageVersion Include="Trax.Scheduler" Version="1.61.0" />
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
    <PackageReference Include="Trax.Api.Dashboard" />
    <PackageReference Include="Trax.Api.GraphQL" />
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.Postgres" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Mediator" />
    <PackageReference Include="Trax.Scheduler" />
  </ItemGroup>
</Project>
```

## Open the namespace to staff, and serve the console

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Trax.Api.Auth.Jwt;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

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
        .AddScheduler(scheduler => scheduler)
);
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<ShopfrontDbContext>()
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: Roles.Staff)
);

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

if (app.Environment.IsDevelopment())
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

await app.RunAsync();
```

What changed:

- **`AddScheduler`** comes straight after `AddMediator`. It registers the operations service every
  `operations` field reads through. With nothing scheduled it schedules nothing; it also starts a
  worker that runs queued trains, which nothing queues yet.
- **`ExposeOperationQueries` and `ExposeOperationMutations`** add the `operations` namespace: the
  runs, the trains, the hosts, and the actions an operator takes, such as cancelling or
  re-queueing a run. Both are off by default, because they reveal and control the whole
  deployment.
- **`GateOperations(roles: Roles.Staff)`** is the gate. Every field under `operations` requires the
  `Staff` role, on every request and on every subscription. Leave the gate out and the host
  refuses to start, naming the three ways to answer it.
- **`UseTraxApiDashboard`** serves the console at `/trax/dashboard`. It also refuses to start
  until it is told who may load the page.

## Who may load the page

The page is the console's code, not its data, and a browser loading a page sends cookies, never
the bearer token Shopfront signs callers in with. A posture of `RequireRoles(Roles.Staff)` would
therefore refuse every load, staff included. Shopfront has no cookie sign-in, so the shell is
anonymous, `AllowAnonymousShell()`, and the gate on the data does the work: anyone can load an
empty console, and only staff get anything into it. The host logs a warning saying so on every
start, which is the point of the option.

A host with a cookie or OpenID Connect sign-in gates the page as well, with `RequirePolicy` or
`RequireRoles`. Shopfront's deployment comes back to it.

## Open it

```bash
dotnet run --urls http://localhost:5080
```

Mint a token for a member of staff and copy it:

```bash
curl -s 'http://localhost:5080/dev/token/lin?staff=true'
```

Open `http://localhost:5080/trax/dashboard` and paste the token when the console asks for a
credential. The console keeps it in the page's memory only: reload the page and it asks again.

**Executions** lists every run, newest first, with its state and, for a failed one, the junction
and the reason. These are the rows `psql` showed in chapter 3. Open a failed order from chapter 3,
the declined card:

- **Input** and **Output** are the stored JSON. The receipt link on a completed order's output is
  `{"_redacted": true}`, as `[TraxSensitive]` made it.
- **Failure** names `TakePaymentJunction`, the message, and the class, `Permanent`.
- **Steps** is the run's junctions in order, each with its timing, the step that failed marked.
- **Decisions** shows the fraud check of every run since chapter 5: the live rule's answer, the
  shadow's, and whether they agreed.

**Trains** lists `PlaceOrder` with its input type and the attributes on it. **Cluster** lists the
process that ran each run.

Leave the console open on **Executions** and place an order as a customer. It appears at the top
as it runs.

## A customer gets nothing

The console's requests are ordinary GraphQL, so the gate is easy to see from `curl`. As staff:

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"{ operations { executions(take: 2) { items { name trainState failureJunction } } } }"}'
```

```json
{"data":{"operations":{"executions":{"items":[{"name":"Shopfront.Trains.PlaceOrder.IPlaceOrderTrain","trainState":"COMPLETED","failureJunction":null},{"name":"Shopfront.Trains.PlaceOrder.IPlaceOrderTrain","trainState":"FAILED","failureJunction":"TakePaymentJunction"}]}}}}
```

As a customer, the same request is refused:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $ADA" \
  -H 'Content-Type: application/json' \
  -d '{"query":"{ operations { executions(take: 2) { items { name } } } }"}'
```

```json
{"errors":[{"message":"Not authorized.","path":["operations"],"extensions":{"code":"TRAX_AUTHORIZATION"}}],"data":{"operations":null}}
```

The refusal names no role and no field below `operations`: a caller who may not see the namespace
learns nothing about what is in it.

## Test it

The host tests check the gate both ways, and that the console is served.

`tests/Shopfront.Tests/IntegrationTests/ConsoleTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/ConsoleTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class ConsoleTests
{
    private const string Executions =
        "{ operations { executions(take: 10) { items { name trainState } } } }";

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

    [Test]
    public async Task Console_IsServed()
    {
        var response = await _app.CreateClient().GetAsync("/trax/dashboard/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Executions_Staff_ReadEveryRun()
    {
        await SignedIn("ada", Roles.Customer)
            .Post(
                "mutation P($input: PlaceOrderInput!) { dispatch { placeOrder(input: $input) { externalId } } }",
                new
                {
                    input = new
                    {
                        paymentToken = "tok_visa",
                        lines = new[] { new { sku = "MUG", quantity = 1 } },
                    },
                }
            );

        var response = await SignedIn("lin", Roles.Staff).Post(Executions);

        response.ErrorCodes().Should().BeEmpty();
        response
            .GetProperty("data")
            .GetProperty("operations")
            .GetProperty("executions")
            .GetProperty("items")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .Should()
            .Contain("Shopfront.Trains.PlaceOrder.IPlaceOrderTrain");
    }

    [Test]
    public async Task Executions_Customer_IsNotAuthorized()
    {
        var response = await SignedIn("ada", Roles.Customer).Post(Executions);

        response.ErrorCodes().Should().Contain("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task Executions_Anonymous_IsNotAuthorized()
    {
        var response = await _app.CreateClient().Post(Executions);

        response.ErrorCodes().Should().Contain("TRAX_AUTHORIZATION");
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    55, Skipped:     0, Total:    55, Duration: 1 m 8 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Staff can read everything Trax recorded about every run, in a console that reaches it through the
same schema the storefront will use, behind a gate that customers and anonymous callers cannot
pass.

That ends the API part. Shopfront answers customers as they ask, and staff can see what happened.
The next part adds work nobody asks for: jobs that run on a schedule, retry when they fail, and
run on a process of their own.

## SDK Reference

> [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Queries](/docs/sdk-reference/graphql-api/queries)
