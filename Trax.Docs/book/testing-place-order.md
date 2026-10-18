---
title: "2. Testing PlaceOrder"
description: "Chapter 2 of the Trax book: add a test project to Shopfront and test each junction, the PlaceOrder train and the running host, with the architecture guards."
type: tutorial
area: general
parent: The Trax book
nav_order: 12
snippet_context: none
follows: book/an-order-is-a-train
---

# 2. Testing PlaceOrder

In this chapter we give Shopfront a test project. By the end, `dotnet test` checks each junction on
its own, runs `PlaceOrder` the way the application does and reads back the record Trax kept, starts
the whole application and calls `POST /orders`, and runs the guards Trax ships for a project's
shape.

From here on, every chapter adds tests for what it builds.

This chapter starts from the end of [chapter 1](/docs/book/an-order-is-a-train). Every C# block on
this page is compiled in CI together with the chapters before it.

## Three kinds of test

| Test | What it builds | What it catches |
|---|---|---|
| Unit | One junction, with its dependencies passed in by hand | A junction that computes the wrong thing |
| Train | The train, through the same `AddTrax` registration as `Program.cs` | A chain that does not run as a whole, a run that is not recorded |
| Host | The whole application, in memory, called over HTTP | A `Program.cs` that no longer starts, an endpoint that answers wrongly |

Most tests are unit tests: they are fast and say exactly what broke. A few train and host tests
prove the pieces still fit.

## Add a solution and a test project

The tests live in a project of their own under `tests/`, which chapter 1's project file already
leaves out of the application. A solution file at the root holds both projects:

```bash
dotnet new sln -n Shopfront
mkdir -p tests/Shopfront.Tests
```

Add the test packages to `Directory.Packages.props`, beside the Trax ones:

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
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator.Testing" Version="1.61.0" />
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

`tests/Shopfront.Tests/Shopfront.Tests.csproj`:

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
    <PackageReference Include="Trax.Mediator.Testing" />
  </ItemGroup>
</Project>
```

Then add both projects to the solution:

```bash
dotnet sln add Shopfront.csproj tests/Shopfront.Tests/Shopfront.Tests.csproj
```

The junctions and the types they pass each other are `internal`, so nothing in the application
outside the train can depend on them. The tests are the one exception, and the application says
so. Add one line to `Shopfront.csproj`:

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
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.InMemory" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>
</Project>
```

`tests/Shopfront.Tests/GlobalUsings.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/GlobalUsings.cs
global using AwesomeAssertions;
global using NUnit.Framework;
```

By the end of the chapter the test project looks like this:

```text
tests/
└── Shopfront.Tests/
    ├── Shopfront.Tests.csproj
    ├── GlobalUsings.cs
    ├── ArchitectureGuards.cs
    ├── UnitTests/
    │   ├── PriceCartJunctionTests.cs
    │   ├── ReserveStockJunctionTests.cs
    │   └── TakePaymentJunctionTests.cs
    └── IntegrationTests/
        ├── PlaceOrderTrainTests.cs
        └── HostTests.cs
```

Test names say what is called, in what situation, and what should happen:
`Run_EmptyCart_Throws`.

## Test a junction

A junction is a class with a `Run` method, so it is tested by constructing it and calling `Run`.
There is no container, no database and no host.

`tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class PriceCartJunctionTests
{
    private static PriceCartJunction Junction() =>
        new(new Catalogue(), NullLogger<PriceCartJunction>.Instance);

    private static PlaceOrderInput Order(params OrderLine[] lines) =>
        new() { CustomerId = "ada", Lines = [.. lines] };

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

`NullLogger` stands in for the logger, which the test does not read. The catalogue is the real one,
because it is a plain class with known contents: a fake would only repeat them.

`tests/Shopfront.Tests/UnitTests/ReserveStockJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ReserveStockJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ReserveStockJunctionTests
{
    private static PricedCart Cart(params PricedLine[] lines) =>
        new([.. lines], lines.Sum(l => l.UnitPrice * l.Quantity));

    [Test]
    public async Task Run_InStock_ReservesEveryLine()
    {
        var catalogue = new Catalogue();
        var junction = new ReserveStockJunction(
            catalogue,
            NullLogger<ReserveStockJunction>.Instance
        );

        await junction.Run(Cart(new PricedLine("TEE", 3, 25.00m)));

        catalogue.TryReserve([("TEE", 1)]).Should().Be("TEE", "all three T-shirts are reserved");
    }

    [Test]
    public async Task Run_OneLineOutOfStock_ThrowsAndReservesNothing()
    {
        var catalogue = new Catalogue();
        var junction = new ReserveStockJunction(
            catalogue,
            NullLogger<ReserveStockJunction>.Instance
        );

        var act = () => junction.Run(Cart(new PricedLine("MUG", 1, 12.00m), new PricedLine("CAP", 1, 18.50m)));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        catalogue.TryReserve([("MUG", 10)]).Should().BeNull("the mug was not reserved either");
    }
}
```

The second test checks the promise chapter 1 made: when one line is short, nothing is reserved.
It asks the catalogue for all ten mugs afterwards, which only succeeds if none were taken.

The payment provider is an interface, and the test needs to know what it was asked to do, so here a
fake earns its place. It records each call and answers with a fixed transaction id.

`tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class TakePaymentJunctionTests
{
    private sealed class RecordingPaymentProvider : IPaymentProvider
    {
        public List<(string CustomerId, decimal Amount)> Charges { get; } = [];

        public Task<string> Charge(
            string customerId,
            decimal amount,
            CancellationToken cancellationToken
        )
        {
            Charges.Add((customerId, amount));
            return Task.FromResult("tx_test");
        }
    }

    [Test]
    public async Task Run_PricedCart_ChargesTheCustomerTheTotal()
    {
        var payments = new RecordingPaymentProvider();
        var junction = new TakePaymentJunction(payments, NullLogger<TakePaymentJunction>.Instance);
        var order = new PlaceOrderInput { CustomerId = "ada", Lines = [new("MUG", 2)] };
        var cart = new PricedCart([new("MUG", 2, 12.00m)], 24.00m);

        var payment = await junction.Run((order, cart));

        payments.Charges.Should().Equal(("ada", 24.00m));
        payment.Should().Be(new Payment("tx_test", 24.00m));
    }
}
```

## Test the train

A unit test cannot tell that the junctions fit together, or that the run was recorded. A train
test builds a container with the same Trax registration as `Program.cs`, runs `PlaceOrder` through
the train bus as any caller would, and reads back the record.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
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
    private ServiceProvider _provider = null!;

    [SetUp]
    public void BuildTheContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Catalogue>();
        services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    [Test]
    public async Task RunAsync_InStock_ConfirmsTheOrderAndRecordsTheRun()
    {
        // A customer no other test uses, to find this run's record among any others.
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(
            new PlaceOrderInput { CustomerId = customerId, Lines = [new("MUG", 2)] }
        );

        output.Total.Should().Be(24.00m);
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId);
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAndRecordsWhy()
    {
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunAsync<PlaceOrderOutput>(
                new PlaceOrderInput { CustomerId = customerId, Lines = [new("CAP", 1)] }
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureJunction.Should().Be("ReserveStockJunction");
        run.FailureReason.Should().Be("CAP is out of stock.");
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

Each run is recorded under the full name of `IPlaceOrderTrain`, with its input and output as JSON,
because chapter 1 turned on `SaveTrainParameters`. A failed run also records the junction that
threw and its message. Chapter 3 reads the same records from Postgres.

The container is built and disposed around every test, so each test starts with an empty record
and a full catalogue.

## Test the host

A host test starts the whole application in memory, as `dotnet run` would, and calls it over
HTTP. It is the test that fails when `Program.cs` no longer starts or an endpoint answers with the
wrong status.

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Trains.PlaceOrder;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class HostTests
{
    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public void StartTheApplication() => _app = new WebApplicationFactory<Program>();

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    [Test]
    public async Task PostOrders_InStock_Answers200WithTheOrder()
    {
        var response = await _app.CreateClient()
            .PostAsJsonAsync(
                "/orders",
                new { customerId = "ada", lines = new[] { new { sku = "MUG", quantity = 2 } } }
            );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<PlaceOrderOutput>();
        order!.Total.Should().Be(24.00m);
    }

    [Test]
    public async Task PostOrders_OutOfStock_Answers422WithTheReason()
    {
        var response = await _app.CreateClient()
            .PostAsJsonAsync(
                "/orders",
                new { customerId = "ada", lines = new[] { new { sku = "CAP", quantity = 1 } } }
            );

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("CAP is out of stock.");
    }
}
```

`WebApplicationFactory<Program>` runs `Program.cs` with a test server in place of Kestrel. The
`InternalsVisibleTo` line is what lets the tests name `Program` at all.

The requests are anonymous objects rather than `PlaceOrderInput`, on purpose: they send the JSON a
real client sends, so a renamed property shows up here as a failing test instead of passing on
both sides.

## Guard the project's shape

Some rules are about the whole project rather than one test: every train has an interface to be
called and recorded by, and tests do not sleep, skip or assert the old way. Trax ships those
checks as test fixtures. A project subclasses them and writes no test bodies.

`tests/Shopfront.Tests/ArchitectureGuards.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/ArchitectureGuards.cs
using System.Reflection;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Testing.Fixtures;
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
```

`TrainGuardFixture` fails when a train has no companion interface. `HygieneGuardFixture` reads the
source under `tests/`, relative to the folder holding the solution, and fails on an `[Ignore]`, a
fixed `Task.Delay` or `Thread.Sleep`, and NUnit's `Assert.That`. That last one is why these tests
assert with AwesomeAssertions: its failures say what was expected and why, in the words of the
`because` argument.

## Run them

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 816 ms - Shopfront.Tests.dll (net10.0)
```

Eleven are ours. The other five are the guards, which the fixtures bring with them.

Break something to see a test catch it. Change the empty-cart message in `PriceCartJunction` and
run the tests again:

```text
  Failed Run_EmptyCart_Throws [13 ms]
  Error Message:
   Expected exception message to match the equivalent of "The cart is empty.", but "Your cart is empty." does not.
```

Put the message back before going on.

## What we have

Shopfront has a test project that checks each junction on its own, the train as a whole and the
application over HTTP, and the guards check its shape. Every later chapter adds to it.

The record the train test reads lives in memory and is gone when the process ends. In chapter 3 we
move it to Postgres, make the payment provider fail now and then, and read back what happened.

## SDK Reference

> [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [Metadata](/docs/effect/metadata) | [Mediator testing](/docs/sdk-reference/testing/mediator-testing) | [Architecture guards](/docs/reference/architecture-guards)
