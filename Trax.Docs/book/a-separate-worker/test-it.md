---
title: "11.9 Test it"
description: "Chapter 11 of the Trax book, section 9: Test it."
type: tutorial
area: general
parent: "11. A separate worker"
grand_parent: The Trax book
nav_order: 9
snippet_context: none
follows: book/a-separate-worker/kill-the-worker-mid-run
---

# 11.9 Test it

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

---

Next: [12. Checkout is a state machine](/docs/book/checkout-is-a-state-machine)
