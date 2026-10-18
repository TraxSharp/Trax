---
title: "12. Checkout is a state machine"
description: "Chapter 12 of the Trax book: write checkout as a state machine with guards, a timer and a rule on every event, and serve it as a typed GraphQL API."
type: tutorial
area: general
parent: The Trax book
nav_order: 22
snippet_context: none
follows: book/a-separate-worker
---

# 12. Checkout is a state machine

So far an order arrives whole: lines, payment token and all, in one request. A real checkout takes
several steps, in an order, with rules about which step may come next: nothing can be paid for
until there is something in the cart and somewhere to send it, and a cart left alone for two hours
is cleared. In this chapter checkout becomes a **state machine**: a set of states, the events that
move between them, and the rules on each move, which the server applies and stores.

This chapter starts from the end of [chapter 11](/docs/book/a-separate-worker).

## The flow

```text
             SetLines (self)          Back
               ┌────┐            ┌─────────────┐
               ▼    │            │             │
  start ──► Cart ───┴──SetAddress──► Address ──┘
             │                        │
             │ 2 hours idle            │ ChoosePayment
             ▼                        ▼
         Abandoned ──StartOver──► Cart   Payment ──PlaceOrder──► Placing
```

Each move is an **event**, sent by the customer. Each state holds a **context**, the data the
checkout has gathered so far. An event can carry an input, check it with a guard, write part of it
into the context, and move to a state. One that breaks a rule is refused with the reason, and the
checkout stays where it was.

`Placing` is where the order is placed. In this chapter checkout stops there; the next chapter
makes it run `PlaceOrder`.

## Where it lives

A machine gets a folder, as a train does, in the library beside the trains
([Organize machines in a project](/docs/statemachine/organize-machines)):

```text
Shopfront.Core/
└── Machines/
    └── Checkout/
        ├── CheckoutMachine.cs     the states, the events and the rules on them
        ├── CheckoutState.cs
        ├── CheckoutTrigger.cs
        ├── CheckoutContext.cs     what a checkout holds
        └── Models/                each event's input, and what the context is made of
```

## Packages

The library takes `Trax.Effect.StateMachine.Persistence`, which holds machines and stores their
instances. The hub serves them, which needs persisted operations, and reports a run's progress
while a machine waits on it. The tests check the machine against its exported definition.

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Api.Auth" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.Auth.Jwt" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.Dashboard" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.GraphQL" Version="1.61.0" />
    <PackageVersion Include="Trax.Api.GraphQL.PersistedOperations" Version="1.61.0" />
    <PackageVersion Include="Trax.Core" Version="1.61.0" />
    <PackageVersion Include="Trax.Core.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Postgres" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Logging" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Progress" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.StateMachine.Persistence" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.StateMachine.Testing" Version="1.61.0" />
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

```xml file=Shopfront.Core/Shopfront.Core.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- The junctions are internal; their tests are the one project that may see them. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <!-- The two the web project's implicit usings gave the code that moved here. -->
    <Using Include="Microsoft.Extensions.DependencyInjection" />
    <Using Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Trax.Api.Auth" />
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.Postgres" />
    <PackageReference Include="Trax.Effect.StateMachine.Persistence" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>

  <ItemGroup>
    <!-- Each machine's committed corpus, which it replays to check itself. -->
    <EmbeddedResource
      Include="..\machines\checkout\differential.json"
      LogicalName="machines/checkout/differential.json"
      Condition="Exists('..\machines\checkout\differential.json')"
    />
  </ItemGroup>
</Project>
```

```xml file=Shopfront.Hub/Shopfront.Hub.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- The host tests start the hub. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Shopfront.Core\Shopfront.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Api.Auth.Jwt" />
    <PackageReference Include="Trax.Api.Dashboard" />
    <PackageReference Include="Trax.Api.GraphQL" />
    <PackageReference Include="Trax.Api.GraphQL.PersistedOperations" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Progress" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Scheduler" />
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
    <ProjectReference Include="..\..\Shopfront.Hub\Shopfront.Hub.csproj" />
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
    <PackageReference Include="Trax.Effect.StateMachine.Testing" />
    <PackageReference Include="Trax.Mediator.Testing" />
  </ItemGroup>
</Project>
```

## States, events and context

`Shopfront.Core/Machines/Checkout/CheckoutState.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutState.cs
namespace Shopfront.Machines.Checkout;

public enum CheckoutState
{
    Cart,
    Address,
    Payment,
    Placing,
    Abandoned,
}
```

`Shopfront.Core/Machines/Checkout/CheckoutTrigger.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutTrigger.cs
namespace Shopfront.Machines.Checkout;

public enum CheckoutTrigger
{
    SetLines,
    SetAddress,
    Back,
    ChoosePayment,
    PlaceOrder,
    StartOver,
}
```

The context holds what the customer chose and nothing the server works out. There are no prices
in it: `PlaceOrder` prices the cart from the catalogue when the order is placed, so a price can
never be sent by the browser, and there is no total to fall out of step with it. The payment is
the token from chapter 3, a reference only the provider can use. A context is stored as plain JSON
and served to its owner, so a card number has no place in it.

`Shopfront.Core/Machines/Checkout/Models/CartLine.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/CartLine.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record CartLine
{
    public string Sku { get; init; } = "";
    public int Quantity { get; init; }
}
```

`Shopfront.Core/Machines/Checkout/CheckoutContext.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutContext.cs
using Shopfront.Machines.Checkout.Models;

namespace Shopfront.Machines.Checkout;

public sealed record CheckoutContext
{
    public List<CartLine>? Lines { get; init; }
    public string? Address { get; init; }
    public string? PaymentToken { get; init; }
}
```

Each event's input is a record of its own.

`Shopfront.Core/Machines/Checkout/Models/CheckoutStart.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/CheckoutStart.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record CheckoutStart;
```

`Shopfront.Core/Machines/Checkout/Models/LinesInput.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/LinesInput.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record LinesInput
{
    public List<CartLine> Lines { get; init; } = [];
}
```

`Shopfront.Core/Machines/Checkout/Models/AddressInput.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/AddressInput.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record AddressInput
{
    public string Address { get; init; } = "";
}
```

`Shopfront.Core/Machines/Checkout/Models/PaymentInput.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/PaymentInput.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record PaymentInput
{
    public string PaymentToken { get; init; } = "";
}
```

## The machine

`Shopfront.Core/Machines/Checkout/CheckoutMachine.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutMachine.cs
using System.Text.Json.Nodes;
using Shopfront.Auth;
using Shopfront.Machines.Checkout.Models;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Shopfront.Machines.Checkout;

[TraxMachine]
public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    private static readonly Rule HasLines = Field((CheckoutContext c) => c.Lines).CountAtLeast(1);

    private static readonly Lazy<string?> EmbeddedCorpus = new(() =>
    {
        using var stream = typeof(CheckoutMachine).Assembly.GetManifestResourceStream(
            "machines/checkout/differential.json"
        );
        return stream is null ? null : new StreamReader(stream).ReadToEnd();
    });

    // The committed corpus, embedded in the library, so the machine can replay it and check itself.
    public override string? Corpus => EmbeddedCorpus.Value;

    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout")
            .Version(1)
            .StartsAt(CheckoutState.Cart, () => new JsonObject())
            .Start<CheckoutStart>(start => start.Reduce(Keep()).Requires(Roles.Customer))
            .Get(read => read.OwnerOnly())
            .Watch(read => read.OwnerOnly())
            .Discard(read => read.OwnerOnly());

        m.In(CheckoutState.Cart)
            .Context<CheckoutContext>()
            .After(TimeSpan.FromHours(2))
            .GoTo(CheckoutState.Abandoned)
            .On(CheckoutTrigger.SetLines)
            .AnyAuthenticated()
            .WithInput<LinesInput>()
            .Reduce(Set((CheckoutContext c) => c.Lines).FromInput((LinesInput i) => i.Lines))
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.SetAddress)
            .AnyAuthenticated()
            .WithInput<AddressInput>()
            .When(All(HasLines, Input((AddressInput i) => i.Address).NonEmpty()))
            .Because("Put something in the cart and give an address to send it to.")
            .Reduce(Set((CheckoutContext c) => c.Address).FromInput((AddressInput i) => i.Address))
            .To(CheckoutState.Address);

        m.In(CheckoutState.Address)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Back)
            .AnyAuthenticated()
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.ChoosePayment)
            .AnyAuthenticated()
            .WithInput<PaymentInput>()
            .When(Input((PaymentInput i) => i.PaymentToken).NonEmpty())
            .Because("Choose how to pay.")
            .Reduce(
                Set((CheckoutContext c) => c.PaymentToken).FromInput((PaymentInput i) => i.PaymentToken)
            )
            .To(CheckoutState.Payment);

        m.In(CheckoutState.Payment)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Back)
            .AnyAuthenticated()
            .To(CheckoutState.Address)
            .On(CheckoutTrigger.PlaceOrder)
            .Requires(Roles.Customer)
            .To(CheckoutState.Placing);

        m.In(CheckoutState.Placing).Context<CheckoutContext>();

        m.In(CheckoutState.Abandoned)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.StartOver)
            .AnyAuthenticated()
            .Reduce(Reset())
            .To(CheckoutState.Cart);

        m.Differential(d =>
            d.Sample(
                    CheckoutTrigger.SetLines,
                    new LinesInput { Lines = [new CartLine { Sku = "MUG", Quantity = 2 }] }
                )
                .Sample(CheckoutTrigger.SetAddress, new AddressInput { Address = "1 Main St" })
                .Sample(CheckoutTrigger.SetAddress, new AddressInput())
                .Sample(CheckoutTrigger.ChoosePayment, new PaymentInput { PaymentToken = "tok_visa" })
                .EmptySample(CheckoutTrigger.ChoosePayment)
        );
    }
}
```

From the top:

- **`Id("checkout")`** names the machine. It names the GraphQL field, the stored instances and every
  file generated from it. **`Version(1)`** is bumped when the machine changes shape.
- **`Start<CheckoutStart>`** says who may start a checkout: a customer. A started checkout belongs to
  whoever started it.
- **`Get`, `Watch` and `Discard`** say who may read it, follow it and throw it away: its owner and
  nobody else. The host refuses to serve a machine that leaves any of them, or any event, without
  a rule, and refuses one looser than the owner for the three reads.
- **`In(state).Context<T>()`** opens a state and says what its context holds.
- **`After(TimeSpan.FromHours(2)).GoTo(...)`** is a timer. A cart left alone for two hours moves to
  `Abandoned`. Every entry to `Cart` restarts it, so each `SetLines` buys the customer another two
  hours. The server fires it, with or without anyone watching.
- **`On(trigger)`** opens an event, and what follows is its rule, its input, its guard, its
  reduction and its target:
  - **`AnyAuthenticated()`** or **`Requires(Roles.Customer)`** is who may send it. Only placing the
    order needs the customer role. Every state a role-gated event enters is reserved, kept from
    being discarded or expiring, so the cart's own events need only a signed-in owner.
  - **`When(rule).Because(text)`** is a guard: the event is refused, with that text, unless the
    rule holds. Rules are data, `Field`, `Input`, `All`, not code, so the server can export them.
  - **`Reduce(Set(...).FromInput(...))`** writes part of the input into the context. `Reset()`
    empties it; `Keep()` leaves it as it is.
- **`Differential`** lists sample inputs the definition is checked with, and **`Corpus`** hands
  the machine the answers to them, both below.

`SetLines` replaces the whole cart. There is no rule for "add one line": the browser keeps the cart
and sends it entire, which keeps the machine to rules the server can export and check, with no
code of its own.

## Serve it

Mark a machine `[TraxMachine]` and the hub serves it as typed GraphQL: `start`, one mutation per
event, `get`, and a subscription that pushes every change. The server applies every event; the
browser only asks.

A served machine's operations are persisted documents the host generates, and a host that serves
one must run persisted operations. Shopfront turns them on now, in **shadow mode**: every document
anyone sends still runs, and each one the host does not hold as a persisted document is logged.
That is the first step of turning them on, and it stays until the storefront exists to say which
documents it sends.

Instances are owned by a user key, which the host works out from the signed-in caller. The hub
says how, in one class.

`Shopfront.Hub/Auth/TraxCallerSnapshotPrincipal.cs`:

```csharp compile=app file=Shopfront.Hub/Auth/TraxCallerSnapshotPrincipal.cs
using Trax.Api.Auth;
using Trax.Effect.StateMachine.Persistence;

namespace Shopfront.Auth;

// A machine instance belongs to the signed-in caller, by their qualified id.
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
```

`Shopfront.Hub/Program.cs`:

```csharp compile=app file=Shopfront.Hub/Program.cs
using Shopfront;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Staff;
using Shopfront.Suppliers;
using Shopfront.Trains.LowStockCheck;
using Shopfront.Trains.Restock;
using Shopfront.Trains.StockReport;
using Trax.Api.Auth.Jwt;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");
var development = builder.Environment.IsDevelopment();

builder.Services.AddShopfront(connectionString);

if (development)
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
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddJunctionProgress()
                .AddDecisionRecording()
                .AddJunctionEvents()
                .AddLifecycleHook<StaffFailureHook>()
        )
        .AddStateMachines(typeof(ManifestNames).Assembly)
        .AddMediator(typeof(ManifestNames).Assembly)
        .AddScheduler(scheduler =>
        {
            // The hub hands every job to the worker through Postgres and runs none itself.
            scheduler.OverrideSubmitter(services =>
                services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()
            );

            // In Development, retries come seconds apart, and a run whose worker died is given up
            // on after a minute rather than an hour, to be watched.
            if (development)
                scheduler
                    .DefaultRetryDelay(TimeSpan.FromSeconds(5))
                    .MaxRetryDelay(TimeSpan.FromSeconds(30))
                    .DefaultJobTimeout(TimeSpan.FromSeconds(30))
                    .StaleInProgressTimeout(TimeSpan.FromMinutes(1));

            return scheduler
                .Schedule<IStockReportTrain>(
                    ManifestNames.StockReport,
                    new StockReportInput(),
                    Cron.Daily(hour: 2),
                    options =>
                        options
                            // The shop's holidays: no count, and none the morning after.
                            .Exclude(
                                Exclude.Dates(new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 1))
                            )
                            .OnMisfire(MisfirePolicy.DoNothing)
                            .Variance(TimeSpan.FromMinutes(10))
                )
                .ThenInclude<ILowStockCheckTrain>(
                    ManifestNames.LowStockCheck,
                    new LowStockCheckInput { Threshold = 3 }
                )
                .Schedule<IRestockTrain>(
                    ManifestNames.Restock,
                    new RestockInput { Threshold = 3 },
                    development ? Every.Minutes(1) : Every.Hours(1),
                    options => options.MaxRetries(2)
                );
        })
);
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<ShopfrontDbContext>()
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: Roles.Staff)
        // Shadow mode: everything runs, and what would be refused is logged.
        .UsePersistedOperations(persisted =>
            persisted.SingleNode().RequirePersisted(false).LogNonPersistedRequests(true)
        )
);

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

if (development)
{
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

    // Takes this process's fake supplier down, or brings it back.
    app.MapPost(
            "/dev/supplier/{state}",
            (string state, FakeSupplier supplier) =>
            {
                supplier.IsDown = state == "down";
                return Results.NoContent();
            }
        )
        .AllowAnonymous();
}

await app.RunAsync();
```

What changed:

- **`AddStateMachines(...)`** finds the library's machines and stores their instances. It comes
  after `AddEffects` and before `AddMediator`.
- **`AddJunctionProgress()`** lets a run be stopped between junctions, which a machine needs once
  it waits on a run.
- **`ISnapshotPrincipal`** is the class above.
- **`UsePersistedOperations(...)`** turns on persisted operations, in shadow mode, on one node.

## Check out

Start the hub and the worker as before, and sign in as `ada`. Start a checkout:

```bash
ADA=$(curl -s http://localhost:5080/dev/token/ada)
G=http://localhost:5080/trax/graphql
curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' \
  -d '{"query":"mutation { machines { checkout { start(requestId: \"r-1\") { id state dueAt } } } }"}'
```

```json
{"data":{"machines":{"checkout":{"start":{"id":"687a7aaa-0807-467d-9093-6e88eb0b4ac0","state":"Cart","dueAt":"2026-10-18T22:46:29.565068Z"}}}}}
```

The checkout is in `Cart`, and `dueAt` is when the two-hour timer fires. Every write carries a
`requestId` of the caller's choosing, so a request sent twice is applied once. Try to give an
address with nothing in the cart:

```bash
ID=<the id>
curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: UUID!) { machines { checkout { setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"r-2\") { state problem { code message } } } } }",
  "variables": { "id": "'$ID'" }
}'
```

```json
{"data":{"machines":{"checkout":{"setAddress":{"state":null,"problem":{"code":"GUARD_FAILED","message":"Put something in the cart and give an address to send it to."}}}}}}
```

The guard refused it with its reason, as data, and the checkout stayed in `Cart`. Put two mugs in
the cart, then give the address, choose how to pay and place the order:

```bash
send() { curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: UUID!) { machines { checkout { '"$1"' { state context { lines { sku quantity } address paymentToken } problem { code } } } } }",
  "variables": { "id": "'$ID'" } }'; echo; }
send 'setLines(id: $id, input: { lines: [{ sku: \"MUG\", quantity: 2 }] }, requestId: \"r-3\")'
send 'setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"r-4\")'
send 'choosePayment(id: $id, input: { paymentToken: \"tok_visa\" }, requestId: \"r-5\")'
send 'placeOrder(id: $id, requestId: \"r-6\")'
```

```json
{"data":{"machines":{"checkout":{"setLines":{"state":"Cart","context":{"lines":[{"sku":"MUG","quantity":2}],"address":null,"paymentToken":null},"problem":null}}}}}
{"data":{"machines":{"checkout":{"setAddress":{"state":"Address","context":{"lines":[{"sku":"MUG","quantity":2}],"address":"1 Main St","paymentToken":null},"problem":null}}}}}
{"data":{"machines":{"checkout":{"choosePayment":{"state":"Payment","context":{"lines":[{"sku":"MUG","quantity":2}],"address":"1 Main St","paymentToken":"tok_visa"},"problem":null}}}}}
{"data":{"machines":{"checkout":{"placeOrder":{"state":"Placing","context":{"lines":[{"sku":"MUG","quantity":2}],"address":"1 Main St","paymentToken":"tok_visa"},"problem":null}}}}}
```

The checkout is in `Placing`, where it waits for the next chapter. As `grace`, the same checkout's
`get` answers `null`, as for a checkout that does not exist: it is `ada`'s.

The hub warns once, at start, that persisted operations are not enforced, and logs each inline
document shadow mode lets through:

```text
warn: Trax.Api.GraphQL.PersistedOperations[0]
      Persisted-operation enforcement is OFF, with shadow logging on. Every inline query this endpoint serves will execute, so per-type [TraxAuthorize] is the only boundary in front of it. If enforcement is keyed on the environment, an environment holding production data but running as Development is running without it.
...
      Trax persisted-operations observed inline query (operationName=(null), willReject=False)
```

`willReject=False` says the host would have run it even with enforcement on. That is the number
to watch while shadow mode lasts.

## Export the definition

The machine is data, so it can be exported: a JSON description of every state, event, guard and
reduction. The browser's copy of the machine in Part 6 is generated from it, and a corpus of sample
inputs, from `Differential`, says what the machine should do with each. Both are generated by the
`trax` tool and committed, so a change to the machine shows up in review as a change to them.

Install the tool for the solution:

```bash
dotnet new tool-manifest
dotnet tool install Trax.Cli --version 1.61.0
```

Build the library, then export:

```bash
dotnet build Shopfront.Core
dotnet trax machine generate --assembly Shopfront.Core/bin/Debug/net10.0/Shopfront.Core.dll \
  --machine Shopfront.Machines.Checkout.CheckoutMachine \
  --ir-out machines/checkout --corpus-out machines/checkout
```

```text
  ir        machines/checkout/checkout.ir.json
  corpus    machines/checkout/differential.json
Generated 2 artifact(s) for 'checkout'.
```

The corpus is worked out by running each sample through the reference engine, the TypeScript one
the browser will use, so `generate` needs Node.js 22 or later on the path. Build the library again
afterwards, so it embeds the corpus it was just given.

`dotnet trax machine check`, with the same arguments, fails when the committed files no longer
match the machine. It belongs in CI.

```text
  ok      ir        machines/checkout/checkout.ir.json
  ok      corpus    machines/checkout/differential.json
All artifacts for 'checkout' are up to date.
```

## Test it

The tests check the machine against the committed files, and drive a checkout through the API as
a customer would.

`tests/Shopfront.Tests/UnitTests/CheckoutMachineTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/CheckoutMachineTests.cs
using Shopfront.Machines.Checkout;
using Trax.Effect.StateMachine.Testing;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class CheckoutMachineTests
{
    // The solution's folder, where machines/ is.
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No solution above the tests.");
    }

    [Test]
    public void Checkout_MatchesItsExportedDefinitionAndCorpus()
    {
        var folder = Path.Combine(Root(), "machines", "checkout");

        MachineConformance
            .Check(
                new CheckoutMachine(),
                Path.Combine(folder, "checkout.ir.json"),
                Path.Combine(folder, "differential.json")
            )
            .Should()
            .BeEmpty("the machine changed without `dotnet trax machine generate`");
    }
}
```

`tests/Shopfront.Tests/IntegrationTests/CheckoutTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/CheckoutTests.cs
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class CheckoutTests
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

    private HttpClient SignedIn(string user)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint(user, Roles.Customer)
        );
        return client;
    }

    private static async Task<JsonElement> Send(HttpClient client, string id, string mutation)
    {
        var response = await client.Post(
            $"mutation($id: UUID!) {{ machines {{ checkout {{ {mutation} {{ state problem {{ code message }} }} }} }} }}",
            new { id }
        );
        return response.GetProperty("data").GetProperty("machines").GetProperty("checkout");
    }

    private static async Task<string> Start(HttpClient client)
    {
        var response = await client.Post(
            "mutation { machines { checkout { start(requestId: \"s\") { id } } } }"
        );
        return response
            .GetProperty("data")
            .GetProperty("machines")
            .GetProperty("checkout")
            .GetProperty("start")
            .GetProperty("id")
            .GetString()!;
    }

    [Test]
    public async Task Checkout_EveryStep_ReachesPlacing()
    {
        var ada = SignedIn("ada");
        var id = await Start(ada);

        await Send(ada, id, "setLines(id: $id, input: { lines: [{ sku: \"MUG\", quantity: 2 }] }, requestId: \"a\")");
        await Send(ada, id, "setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"b\")");
        await Send(ada, id, "choosePayment(id: $id, input: { paymentToken: \"tok_visa\" }, requestId: \"c\")");
        var placed = await Send(ada, id, "placeOrder(id: $id, requestId: \"d\")");

        placed.GetProperty("placeOrder").GetProperty("state").GetString().Should().Be("Placing");
    }

    [Test]
    public async Task SetAddress_EmptyCart_IsRefusedWithTheReason()
    {
        var ada = SignedIn("ada");
        var id = await Start(ada);

        var refused = await Send(
            ada,
            id,
            "setAddress(id: $id, input: { address: \"1 Main St\" }, requestId: \"a\")"
        );

        var answer = refused.GetProperty("setAddress");
        answer.GetProperty("problem").GetProperty("code").GetString().Should().Be("GUARD_FAILED");
        answer
            .GetProperty("problem")
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Put something in the cart and give an address to send it to.");
    }

    [Test]
    public async Task AnotherCustomer_CannotReachTheCheckout()
    {
        var id = await Start(SignedIn("ada"));

        var hers = await Send(
            SignedIn("grace"),
            id,
            "setLines(id: $id, input: { lines: [] }, requestId: \"a\")"
        );

        hers.GetProperty("setLines").GetProperty("state").ValueKind.Should().Be(JsonValueKind.Null, "a checkout is its owner's");
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    70, Skipped:     0, Total:    70, Duration: 58 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Checkout is a machine the server applies: each step a typed mutation, refused with its reason when
it breaks a rule, owned by the customer who started it, cleared by a timer when it is left alone.
Its definition is exported and checked, so the browser's copy of it can be generated from the same
source later.

Checkout stops at `Placing`. The next chapter has it place the order.

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)
