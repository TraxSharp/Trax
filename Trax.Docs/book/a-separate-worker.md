---
title: "11. A separate worker"
description: "Chapter 11 of the Trax book: queue fulfilment after each order, move the trains into a library, and run queued and scheduled work on a worker process."
type: tutorial
area: general
parent: The Trax book
nav_order: 21
snippet_context: none
follows: book/failures-retries-and-dead-letters
moves:
  - Shopfront.csproj -> Shopfront.Hub/Shopfront.Hub.csproj
  - Program.cs -> Shopfront.Hub/Program.cs
  - appsettings.json -> Shopfront.Hub/appsettings.json
  - Auth/DevJwt.cs -> Shopfront.Hub/Auth/DevJwt.cs
  - Auth/ -> Shopfront.Core/Auth/
  - Data/ -> Shopfront.Core/Data/
  - Extensions/ -> Shopfront.Core/Extensions/
  - Failures/ -> Shopfront.Core/Failures/
  - Fraud/ -> Shopfront.Core/Fraud/
  - Inventory/ -> Shopfront.Core/Inventory/
  - Payments/ -> Shopfront.Core/Payments/
  - Staff/ -> Shopfront.Core/Staff/
  - Suppliers/ -> Shopfront.Core/Suppliers/
  - Trains/ -> Shopfront.Core/Trains/
  - ManifestNames.cs -> Shopfront.Core/ManifestNames.cs
---

# 11. A separate worker

Once an order is paid, someone has to pack it. Packing takes a while, and the customer should not
wait for it, so `PlaceOrder` queues it and answers. In this chapter fulfilment becomes queued work,
and every piece of work nobody waits for, the queued packing and the scheduled jobs from the last
two chapters, moves to a process of its own: a worker. The process customers talk to only takes
requests and schedules.

Then we kill the worker in the middle of packing an order, and see what Trax does about it.

This chapter starts from the end of [chapter 10](/docs/book/failures-retries-and-dead-letters).

## One library, two processes

Two processes that run the same trains need the same trains. They move into a class library,
`Shopfront.Core`, and each process becomes a thin executable that references it and says how it
runs: the **hub** serves the API and the console and decides what is due; the **worker** runs it.

```text
Shopfront/
├── Directory.Packages.props
├── Shopfront.slnx
├── Shopfront.Core/          the trains, their data, the shop: everything both processes run
├── Shopfront.Hub/           the API, the console and the scheduler
├── Shopfront.Worker/        runs queued and scheduled work
└── tests/
    └── Shopfront.Tests/
```

Move the code, from the solution's folder:

```bash
mkdir -p Shopfront.Core Shopfront.Hub/Auth Shopfront.Worker
mv Auth/DevJwt.cs Shopfront.Hub/Auth/
mv Auth Data Extensions Failures Fraud Inventory Payments Staff Suppliers Trains ManifestNames.cs Shopfront.Core/
mv Program.cs appsettings.json Properties Shopfront.Hub/
mv Shopfront.csproj Shopfront.Hub/Shopfront.Hub.csproj
```

The development signing key goes with the hub, which is the only process that signs anyone in.
Everything else the trains need goes to the library. The namespaces stay as they are: the library's
root namespace is `Shopfront`, so nothing that moved needs editing.

## The projects

`Directory.Packages.props` gains `Trax.Api.Auth`, which the library uses to know the caller without
the API around it:

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

The library is a plain `Microsoft.NET.Sdk` project with the ASP.NET Core framework reference, and
the packages its trains compile against. It has no `Program.cs` and no `appsettings.json`: how it
runs is the executables' to say. A web project imports a few namespaces into every file on its own;
a plain one imports fewer, so the library names the two its code relies on, logging and dependency
injection.

`Shopfront.Core/Shopfront.Core.csproj`:

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
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>
</Project>
```

`Shopfront.Hub/Shopfront.Hub.csproj`:

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
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Scheduler" />
  </ItemGroup>
</Project>
```

The worker needs the library, the effects that record a run, and the scheduler's worker. No
GraphQL, no console, no sign-in.

`Shopfront.Worker/Shopfront.Worker.csproj`:

```xml file=Shopfront.Worker/Shopfront.Worker.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Shopfront.Core\Shopfront.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Scheduler" />
  </ItemGroup>
</Project>
```

`tests/Shopfront.Tests/Shopfront.Tests.csproj` references the hub, which brings the library:

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
    <PackageReference Include="Trax.Mediator.Testing" />
  </ItemGroup>
</Project>
```

The solution lists the new projects:

```bash
dotnet sln remove Shopfront.csproj
dotnet sln add Shopfront.Core/Shopfront.Core.csproj Shopfront.Hub/Shopfront.Hub.csproj \
  Shopfront.Worker/Shopfront.Worker.csproj
```

## Fulfilment is a train

Packing happens in the warehouse, which, like the payment provider and the supplier, is somebody
else's system behind an interface.

`Shopfront.Core/Warehouse/IWarehouse.cs`:

```csharp compile=app file=Shopfront.Core/Warehouse/IWarehouse.cs
namespace Shopfront.Warehouse;

public interface IWarehouse
{
    // Packs the order's items and hands back the parcel's tracking number.
    Task<string> Pack(Guid orderId, int items, CancellationToken cancellationToken);
}
```

`Shopfront.Core/Warehouse/FakeWarehouse.cs`:

```csharp compile=app file=Shopfront.Core/Warehouse/FakeWarehouse.cs
namespace Shopfront.Warehouse;

// Takes ten seconds to pack anything, long enough to kill the worker while it does.
public class FakeWarehouse : IWarehouse
{
    public TimeSpan PackingTime { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<string> Pack(Guid orderId, int items, CancellationToken cancellationToken)
    {
        await Task.Delay(PackingTime, cancellationToken);
        return $"PKG-{orderId.ToString("N")[..8].ToUpperInvariant()}";
    }
}
```

The train takes what packing needs: the order and how many items are in it. It carries no
`[TraxAuthorize]`: it is queued by `PlaceOrder`, not by anyone outside.

`Shopfront.Core/Trains/FulfilOrder/FulfilOrderInput.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/FulfilOrderInput.cs
namespace Shopfront.Trains.FulfilOrder;

public record FulfilOrderInput
{
    public required Guid OrderId { get; init; }
    public required int Items { get; init; }
}
```

`Shopfront.Core/Trains/FulfilOrder/FulfilOrderOutput.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/FulfilOrderOutput.cs
namespace Shopfront.Trains.FulfilOrder;

public record FulfilOrderOutput(Guid OrderId, string TrackingNumber);
```

`Shopfront.Core/Trains/FulfilOrder/IFulfilOrderTrain.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/IFulfilOrderTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.FulfilOrder;

public interface IFulfilOrderTrain : IServiceTrain<FulfilOrderInput, FulfilOrderOutput>;
```

`Shopfront.Core/Trains/FulfilOrder/Junctions/PackOrderJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/Junctions/PackOrderJunction.cs
using Shopfront.Warehouse;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class PackOrderJunction(IWarehouse warehouse, ILogger<PackOrderJunction> logger)
    : EffectJunction<FulfilOrderInput, FulfilOrderOutput>
{
    public override async Task<FulfilOrderOutput> Run(FulfilOrderInput input)
    {
        logger.LogInformation("Packing order {OrderId}", input.OrderId);
        var tracking = await warehouse.Pack(input.OrderId, input.Items, CancellationToken);
        logger.LogInformation("Packed order {OrderId} as {Tracking}", input.OrderId, tracking);
        return new FulfilOrderOutput(input.OrderId, tracking);
    }
}
```

`Shopfront.Core/Trains/FulfilOrder/FulfilOrderTrain.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/FulfilOrderTrain.cs
using Shopfront.Trains.FulfilOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.FulfilOrder;

public class FulfilOrderTrain : ServiceTrain<FulfilOrderInput, FulfilOrderOutput>, IFulfilOrderTrain
{
    protected override Task<Either<Exception, FulfilOrderOutput>> Junctions() =>
        Chain<PackOrderJunction>().Resolve();

    // One order is fulfilled one queued run at a time, however often it is queued.
    protected override string? QueueSubjectKey(Metadata metadata) =>
        $"order:{TrainInput.OrderId}";
}
```

`QueueSubjectKey` names what the queued work is about. Two queued runs with the same key never run
at once: the second waits until the first is done. An order queued twice by mistake is therefore
never packed by two workers side by side.

## PlaceOrder queues it

The last junction of `PlaceOrder` queues fulfilment for the order it just confirmed. It queues
through `ITrainExecutionService`, the same path a GraphQL caller's queued work takes, so the queued
train's own rules apply to it: its authorization, its queue hooks and its subject key.

`Shopfront.Core/Trains/PlaceOrder/Junctions/QueueFulfilmentJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/Junctions/QueueFulfilmentJunction.cs
using System.Text.Json;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainExecution;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class QueueFulfilmentJunction(
    ITrainExecutionService trains,
    ILogger<QueueFulfilmentJunction> logger
) : EffectJunction<(PlaceOrderOutput Order, PricedCart Cart), Unit>
{
    public override async Task<Unit> Run((PlaceOrderOutput Order, PricedCart Cart) input)
    {
        var queued = await trains.QueueAsync(
            typeof(IFulfilOrderTrain).FullName!,
            JsonSerializer.Serialize(
                new FulfilOrderInput
                {
                    OrderId = input.Order.OrderId,
                    Items = input.Cart.Lines.Sum(l => l.Quantity),
                }
            ),
            ct: CancellationToken
        );
        logger.LogInformation(
            "Queued fulfilment of {OrderId} as work queue entry {Entry}",
            input.Order.OrderId,
            queued.WorkQueueId
        );
        return Unit.Default;
    }
}
```

It comes last, after the order is stored, because a queued entry is written at once and is not
undone if the run fails later. It returns `Unit`, so the train's output is still the
`PlaceOrderOutput` the confirm made.

`Shopfront.Core/Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/PlaceOrderTrain.cs
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
            .Chain<QueueFulfilmentJunction>()
            .Resolve();
}
```

The warehouse joins the shop's services.

`Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Fraud;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Staff;
using Shopfront.Suppliers;
using Shopfront.Warehouse;
using Trax.Core.Decisions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Extensions;

public static class ShopfrontServiceCollectionExtensions
{
    // Everything Shopfront's trains need, apart from Trax itself. Every process that runs the
    // trains calls it, because Trax checks every train's chain when a process starts.
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
            .AddSingleton<ICandidateFraudDecider, StricterFraudRules>()
            .AddSingleton<IStaffNotifier, LoggingStaffNotifier>()
            .AddSingleton<FakeSupplier>()
            .AddSingleton<ISupplier>(sp => sp.GetRequiredService<FakeSupplier>())
            .AddSingleton<IWarehouse, FakeWarehouse>();
}
```

A fulfilment is not a manifest, so the failure hook from chapter 10 would not hear about it. Its
rule becomes what it meant: staff hear about every failure but an order's, which is the customer's
to see.

`Shopfront.Core/Staff/StaffFailureHook.cs`:

```csharp compile=app file=Shopfront.Core/Staff/StaffFailureHook.cs
using Shopfront.Trains.PlaceOrder;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Shopfront.Staff;

// Tells staff about every failed run of the work nobody waits for. A failed order is the customer's.
public class StaffFailureHook(IStaffNotifier staff) : ITrainLifecycleHook
{
    public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        metadata.Name == typeof(IPlaceOrderTrain).FullName
            ? Task.CompletedTask
            : staff.Notify($"{metadata.Name} failed: {exception.Message}", ct);
}
```

## The hub

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
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Provider.Parameter.Extensions;
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

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddDecisionRecording()
                .AddJunctionEvents()
                .AddLifecycleHook<StaffFailureHook>()
        )
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

Three things changed:

- **`AddMediator(typeof(ManifestNames).Assembly)`** names the library. The trains are not in the
  hub's assembly any more, and a train in an assembly the mediator does not scan is not registered.
- **`OverrideSubmitter(... PostgresJobSubmitter ...)`** is what makes the hub stop running jobs. A
  scheduler on Postgres runs its own workers by default; naming the submitter hands every job to
  the `background_job` table instead, for a worker to claim. The hub still runs `PlaceOrder`, which
  a customer waits on, itself.
- **`DefaultJobTimeout` and `StaleInProgressTimeout`** are shortened in Development. They come back
  below.

## The worker

`Shopfront.Worker/Program.cs`:

```csharp compile=worker file=Shopfront.Worker/Program.cs
using Shopfront;
using Shopfront.Extensions;
using Shopfront.Staff;
using Trax.Api.Auth;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
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
// The trains ask who the caller is. On the worker nobody is, and that is the answer they get.
builder.Services.AddTraxPrincipalAccessor();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddDecisionRecording()
                .AddJunctionEvents()
                .AddLifecycleHook<StaffFailureHook>()
        )
        .AddMediator(mediator =>
            mediator
                .ScanAssemblies(typeof(ManifestNames).Assembly)
                // The hub authorized every job this worker runs before it was queued.
                .AllowMissingAuthorizationService()
        )
);
builder.Services.AddTraxWorker(worker => worker.WorkerCount = 2);

var app = builder.Build();
await app.RunAsync();
```

- **`AddShopfront`** is the same call the hub makes. When a process starts, Trax checks the chain of
  every train it registered, including the ones it never runs, so every process that registers the
  library's trains registers their services too.
- **`AddTraxPrincipalAccessor`** gives the trains a way to ask who the caller is. The hub gets it
  from its JWT sign-in; the worker has none, and the answer on the worker is nobody.
- **`AllowMissingAuthorizationService`** says this process enforces no `[TraxAuthorize]`. Without it
  the worker refuses to start, because the library holds `PlaceOrder`, which declares one, and
  nothing here would check it. That is safe only because nothing reaches the worker but the jobs
  the hub queued, after the hub checked who queued them.
- **`AddTraxWorker`** claims queued jobs from Postgres and runs them, two at a time. That is the
  cap: at most two fulfilments, or restocks, or any mix, run at once on this worker.

The worker reads the same configuration as the hub.

`Shopfront.Worker/appsettings.json`:

```json file=Shopfront.Worker/appsettings.json
{
  "ConnectionStrings": {
    "Shopfront": "Host=localhost;Database=shopfront;Username=postgres;Password=shopfront"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## Run both

In one terminal, the hub:

```bash
dotnet run --project Shopfront.Hub --urls http://localhost:5080
```

In a second, the worker:

```bash
dotnet run --project Shopfront.Worker --urls http://localhost:5081
```

Place an order as `ada`. `PlaceOrder` answers as soon as the order is confirmed, and the hub's log
shows fulfilment queued:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.QueueFulfilmentJunction[0]
      Queued fulfilment of 8fb0141d-1862-413b-9be1-b3940eef2453 as work queue entry 2
```

The worker's log picks it up and packs it, ten seconds later:

```text
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packing order 8fb0141d-1862-413b-9be1-b3940eef2453
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packed order 8fb0141d-1862-413b-9be1-b3940eef2453 as PKG-8FB0141D
```

In the console, **Cluster** now lists two processes, each named by its machine and process id, with
how many runs each has run. **Executions** records the machine each run ran on; on one machine that
is the same name for both, and on separate machines it tells them apart.

The supplier switch from chapter 10 is still on the hub, and the restock now runs on the worker,
whose fake supplier never hears about it. A fake's state lives in one process; the supplier a real
shop uses does not.

## Kill the worker mid-run

Place another order, and kill the worker while it packs, within the ten seconds. Kill it, not stop
it: Ctrl+C asks the worker to finish what it is running first, which is the point of stopping one.

```bash
pkill -9 -f Shopfront.Worker
```

The fulfilment's run says `In progress` in the console, and stays that way, because nothing is
running it. Start the worker again. It does not pick the run up: the job was claimed and started,
and a job that started is never started twice, since running it again might pack a parcel twice.

The hub sees to it instead. Every run in progress longer than its timeout is given up on, and in
Development that is about a minute. The failure hook tells staff:

```text
warn: Shopfront.Staff.LoggingStaffNotifier[0]
      For staff: Shopfront.Trains.FulfilOrder.IFulfilOrderTrain failed: Job was stuck InProgress beyond the configured stale in-progress timeout
```

Two settings decide when, and both are shortened in Development. `DefaultJobTimeout`, 20 minutes by
default, is how long a job may run before the hub cancels it. `StaleInProgressTimeout`, an hour by
default, is how long after that a run still in progress is failed. Shortening only the second
changes nothing, because the hub waits for the first.

The run is `Failed` now, and its reason says why. A fulfilment has no manifest, so nothing retries
it. A member of staff decides: open the run in the console and choose
**Re-queue**, which runs it again with the input it was given, on whichever worker claims it.

## Test it

Fulfilment's junction is tested with a warehouse that packs at once.

`tests/Shopfront.Tests/UnitTests/PackOrderJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/PackOrderJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.FulfilOrder.Junctions;
using Shopfront.Warehouse;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class PackOrderJunctionTests
{
    [Test]
    public async Task Run_PacksTheOrderAndReturnsItsTrackingNumber()
    {
        var orderId = Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89");
        var junction = new PackOrderJunction(
            new FakeWarehouse { PackingTime = TimeSpan.Zero },
            NullLogger<PackOrderJunction>.Instance
        );

        var parcel = await junction.Run(new FulfilOrderInput { OrderId = orderId, Items = 2 });

        parcel.Should().Be(new FulfilOrderOutput(orderId, "PKG-3179F9C4"));
    }
}
```

The train test checks that a placed order queues its fulfilment, by reading the work queue.

`tests/Shopfront.Tests/IntegrationTests/FulfilmentTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/FulfilmentTests.cs
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Api.Auth;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class FulfilmentTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task BuildTheContainer()
    {
        await TestDatabase.Reset();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShopfront(TestDatabase.ConnectionString);
        services.AddScoped(_ => new TraxPrincipal("TraxJwt:ada", "ada", [Roles.Customer]));
        services.AddScoped<IShopfrontCaller>(_ => TestCaller.Customer("TraxJwt:ada"));
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    [Test]
    public async Task PlaceOrder_Confirmed_QueuesItsFulfilment()
    {
        var bus = _provider.GetRequiredService<ITrainBus>();

        var order = await bus.RunAsync<PlaceOrderOutput>(
            new PlaceOrderInput { Lines = [new OrderLine("MUG", 2)], PaymentToken = "tok_visa" }
        );

        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var queued = data.WorkQueues.Single();
        queued.TrainName.Should().Be(typeof(IFulfilOrderTrain).FullName);
        queued.Input.Should().Contain(order.OrderId.ToString());
    }
}
```

The data-layer guard scans the library's `Data` folder now, relative to the solution.

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
    protected override ArchitectureGuardOptions Options =>
        new() { SourceScanRoots = ["Shopfront.Core/Data"] };

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

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    66, Skipped:     0, Total:    66, Duration: 45 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront runs as two processes over one database: a hub that customers and staff talk to, which
schedules work and queues it, and a worker that runs it, two jobs at a time. An order is packed
after the customer has their answer. A worker that dies mid-run leaves a run that Trax gives up on
and staff can send round again, rather than one that silently never finishes or runs twice.

That ends the third part. The next part goes back to the customer, to the steps before an order is
placed: the cart, the address, the payment, as a state machine.

## SDK Reference

> [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Queued work](/docs/mediator/queued-work) | [Job submission](/docs/scheduler/job-submission) | [Structuring your application](/docs/reference/project-layout) | [Host tracking](/docs/effect/host-tracking)
