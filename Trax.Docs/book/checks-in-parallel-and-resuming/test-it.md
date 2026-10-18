---
title: "14.6 Test it"
description: "Chapter 14 of the Trax book, section 6: Test it."
type: tutorial
area: general
parent: "14. Checks in parallel, and resuming"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/checks-in-parallel-and-resuming/kill-the-worker-mid-pack-and-resume
---

# 14.6 Test it

The junction tests move to the new signatures, and the train is tested whole, branches and
checkpoint included, with fakes that answer at once.

`tests/Shopfront.Tests/UnitTests/PackOrderJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/PackOrderJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.FulfilOrder.Junctions;
using Shopfront.Trains.FulfilOrder.Models;
using Shopfront.Warehouse;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class PackOrderJunctionTests
{
    [Test]
    public async Task Run_PacksTheOrderUnderItsLabel()
    {
        var orderId = Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89");
        var junction = new PackOrderJunction(
            new FakeWarehouse { PackingTime = TimeSpan.Zero },
            NullLogger<PackOrderJunction>.Instance
        );

        var parcel = await junction.Run(
            (
                new FulfilOrderInput { OrderId = orderId, Items = 2 },
                new ShippingLabel("TRK-1", 5.50m)
            )
        );

        parcel.Should().Be(new FulfilOrderOutput(orderId, "TRK-1"));
    }
}
```

`tests/Shopfront.Tests/UnitTests/BuyLabelJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/BuyLabelJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Couriers;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.FulfilOrder.Junctions;
using Shopfront.Warehouse;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class BuyLabelJunctionTests
{
    private static readonly FulfilOrderInput Order = new()
    {
        OrderId = Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89"),
        Items = 2,
    };

    [Test]
    public async Task Run_PricesTheLabelByTheBoxsWeight()
    {
        var junction = new BuyLabelJunction(
            new FakeCourier(),
            NullLogger<BuyLabelJunction>.Instance
        );

        var label = await junction.Run(
            (Order, new Box("Small", 1m), new CourierRates(4.00m, 1.50m))
        );

        label.Price.Should().Be(5.50m);
        label.TrackingNumber.Should().Be("TRK-3179F9C4");
    }

    [Test]
    public async Task Run_Twice_BuysOneLabel()
    {
        var courier = new FakeCourier();
        var junction = new BuyLabelJunction(courier, NullLogger<BuyLabelJunction>.Instance);
        var rates = new CourierRates(4.00m, 1.50m);

        var first = await junction.Run((Order, new Box("Small", 1m), rates));
        var again = await junction.Run((Order, new Box("Large", 3m), rates));

        again
            .TrackingNumber.Should()
            .Be(first.TrackingNumber, "the courier keys a label by its order");
    }
}
```

`tests/Shopfront.Tests/IntegrationTests/FulfilOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/FulfilOrderTrainTests.cs
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Couriers;
using Shopfront.Extensions;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Warehouse;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class FulfilOrderTrainTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public void BuildTheContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShopfront(TestDatabase.ConnectionString);
        // Fakes that answer at once, registered after AddShopfront's, so they win.
        services.AddSingleton<IWarehouse>(
            new FakeWarehouse { ChoosingTime = TimeSpan.Zero, PackingTime = TimeSpan.Zero }
        );
        services.AddSingleton<ICourier>(new FakeCourier { QuoteTime = TimeSpan.Zero });
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(IFulfilOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    [Test]
    public async Task RunAsync_JoinsTheBranchesAndPacksUnderTheLabel()
    {
        var orderId = Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89");
        var bus = _provider.GetRequiredService<ITrainBus>();

        var parcel = await bus.RunAsync<FulfilOrderOutput>(
            new FulfilOrderInput { OrderId = orderId, Items = 3 }
        );

        parcel.Should().Be(new FulfilOrderOutput(orderId, "TRK-3179F9C4"));
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    74, Skipped:     0, Total:    74 - Shopfront.Tests.dll (net10.0)
```

## What we have

Fulfilment asks the warehouse and the courier at once, buys one label with both answers, and
stores the label in a checkpoint. A fulfilment that dies after that resumes from the checkpoint,
from GraphQL or the console, without buying another.

That is the last of the back end. The next part builds the storefront the customers use, in
React, from a client Trax generates.

## SDK Reference

> [Parallel](/docs/sdk-reference/train-methods/parallel) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [Checkpoints and branches](/docs/core/checkpoints-and-branches) | [resumeExecution](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) | [runGraph](/docs/sdk-reference/graphql-api/queries/executions#rungraph)

---

Next: [15. A generated client](/docs/book/a-generated-client)
