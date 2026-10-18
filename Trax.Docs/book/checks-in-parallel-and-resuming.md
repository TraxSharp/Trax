---
title: "14. Checks in parallel, and resuming"
description: "Chapter 14 of the Trax book: look up fulfilment's box and courier rates side by side, and checkpoint the label so a resume never buys another."
type: tutorial
area: general
parent: The Trax book
nav_order: 24
snippet_context: none
follows: book/the-machine-places-the-order
---

# 14. Checks in parallel, and resuming

Fulfilment packs the order, and that is all it does. A real one does more first: it picks a box
for the order, asks the courier what it charges, and buys a label. The box and the rates do not
depend on each other, so they are looked up side by side. The label costs money, so once it is
bought, a fulfilment that fails afterwards picks up from there rather than buying a second one.

At the end of [chapter 11](/docs/book/a-separate-worker) a member of staff re-queued a fulfilment
whose worker died. A re-queue runs the train from its first junction. With a label to buy, that
would buy it twice. In this chapter staff **resume** it instead.

This chapter starts from the end of [chapter 13](/docs/book/the-machine-places-the-order).

## The new chain

```text
FulfilOrderInput
   ├── box:     ChooseBoxJunction        → Box
   └── courier: GetCourierRatesJunction  → CourierRates
BuyLabelJunction                         → ShippingLabel
Checkpoint<ShippingLabel>
PackOrderJunction                        → FulfilOrderOutput
```

The two branches run at once. `BuyLabelJunction` runs when both have finished, with what each
returned. The checkpoint stores the label. `PackOrderJunction` packs the order with the label on
it.

## The courier and the warehouse

The courier is a new fake, beside the payment provider and the supplier. A real courier keys a
label by something the shop gives it, so asking twice for the same order's label returns the
first; the fake does the same, by the order's id.

```text
Shopfront.Core/Couriers/
├── ICourier.cs
├── CourierRates.cs
└── FakeCourier.cs
```

`Shopfront.Core/Couriers/CourierRates.cs`:

```csharp compile=app file=Shopfront.Core/Couriers/CourierRates.cs
namespace Shopfront.Couriers;

public record CourierRates(decimal Base, decimal PerKilogram);
```

`Shopfront.Core/Couriers/ICourier.cs`:

```csharp compile=app file=Shopfront.Core/Couriers/ICourier.cs
namespace Shopfront.Couriers;

public interface ICourier
{
    Task<CourierRates> Rates(CancellationToken cancellationToken);

    // Buys the label for an order and hands back its tracking number. Asking again for the same
    // order hands back the first label, not a second one.
    Task<string> BuyLabel(Guid orderId, decimal price, CancellationToken cancellationToken);
}
```

`Shopfront.Core/Couriers/FakeCourier.cs`:

```csharp compile=app file=Shopfront.Core/Couriers/FakeCourier.cs
using System.Collections.Concurrent;

namespace Shopfront.Couriers;

// Takes two seconds to quote, and remembers every label it sold while the process runs.
public class FakeCourier : ICourier
{
    private readonly ConcurrentDictionary<Guid, string> labels = new();

    public TimeSpan QuoteTime { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<CourierRates> Rates(CancellationToken cancellationToken)
    {
        await Task.Delay(QuoteTime, cancellationToken);
        return new CourierRates(Base: 4.00m, PerKilogram: 1.50m);
    }

    public Task<string> BuyLabel(
        Guid orderId,
        decimal price,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            labels.GetOrAdd(orderId, id => $"TRK-{id.ToString("N")[..8].ToUpperInvariant()}")
        );
}
```

The warehouse picks the box now as well, and packs the order under a tracking number it is given
rather than one it makes up.

`Shopfront.Core/Warehouse/Box.cs`:

```csharp compile=app file=Shopfront.Core/Warehouse/Box.cs
namespace Shopfront.Warehouse;

public record Box(string Size, decimal Kilograms);
```

`Shopfront.Core/Warehouse/IWarehouse.cs`:

```csharp compile=app file=Shopfront.Core/Warehouse/IWarehouse.cs
namespace Shopfront.Warehouse;

public interface IWarehouse
{
    // The box an order of this many items goes in, and what it weighs packed.
    Task<Box> ChooseBox(int items, CancellationToken cancellationToken);

    // Packs the order's items and puts the label on the parcel.
    Task Pack(Guid orderId, int items, string trackingNumber, CancellationToken cancellationToken);
}
```

`Shopfront.Core/Warehouse/FakeWarehouse.cs`:

```csharp compile=app file=Shopfront.Core/Warehouse/FakeWarehouse.cs
namespace Shopfront.Warehouse;

// Takes two seconds to choose a box and ten to pack, long enough to kill the worker while it does.
public class FakeWarehouse : IWarehouse
{
    public TimeSpan ChoosingTime { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PackingTime { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<Box> ChooseBox(int items, CancellationToken cancellationToken)
    {
        await Task.Delay(ChoosingTime, cancellationToken);
        return items <= 2 ? new Box("Small", 1m) : new Box("Large", 3m);
    }

    public Task Pack(
        Guid orderId,
        int items,
        string trackingNumber,
        CancellationToken cancellationToken
    ) => Task.Delay(PackingTime, cancellationToken);
}
```

`AddShopfront` registers the courier with the other fakes. Every process that runs the trains
calls it, so the worker has it too.

`Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Couriers;
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
            .AddSingleton<IWarehouse, FakeWarehouse>()
            .AddSingleton<ICourier, FakeCourier>();
}
```

## The junctions

Fulfilment gets a `Models/` folder for the label, the one value it hands from one junction to the
next that is its own.

```text
Shopfront.Core/Trains/FulfilOrder/
├── IFulfilOrderTrain.cs
├── FulfilOrderTrain.cs
├── FulfilOrderInput.cs
├── FulfilOrderOutput.cs
├── Models/
│   └── ShippingLabel.cs
└── Junctions/
    ├── ChooseBoxJunction.cs
    ├── GetCourierRatesJunction.cs
    ├── BuyLabelJunction.cs
    └── PackOrderJunction.cs
```

`Shopfront.Core/Trains/FulfilOrder/Models/ShippingLabel.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/Models/ShippingLabel.cs
namespace Shopfront.Trains.FulfilOrder.Models;

// The checkpoint stores it as JSON and reads it back, so it is sealed: a derived label would come
// back as this type.
public sealed record ShippingLabel(string TrackingNumber, decimal Price);
```

The two branches each read the input and return a value. Neither writes anything.

`Shopfront.Core/Trains/FulfilOrder/Junctions/ChooseBoxJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/Junctions/ChooseBoxJunction.cs
using Shopfront.Warehouse;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class ChooseBoxJunction(IWarehouse warehouse, ILogger<ChooseBoxJunction> logger)
    : EffectJunction<FulfilOrderInput, Box>
{
    public override async Task<Box> Run(FulfilOrderInput input)
    {
        logger.LogInformation("Choosing a box for order {OrderId}", input.OrderId);
        var box = await warehouse.ChooseBox(input.Items, CancellationToken);
        logger.LogInformation("Chose a {Size} box for order {OrderId}", box.Size, input.OrderId);
        return box;
    }
}
```

`Shopfront.Core/Trains/FulfilOrder/Junctions/GetCourierRatesJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/Junctions/GetCourierRatesJunction.cs
using Shopfront.Couriers;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class GetCourierRatesJunction(ICourier courier, ILogger<GetCourierRatesJunction> logger)
    : EffectJunction<FulfilOrderInput, CourierRates>
{
    public override async Task<CourierRates> Run(FulfilOrderInput input)
    {
        logger.LogInformation("Asking the courier's rates for order {OrderId}", input.OrderId);
        var rates = await courier.Rates(CancellationToken);
        logger.LogInformation(
            "The courier charges {Base} and {PerKilogram} a kilogram",
            rates.Base,
            rates.PerKilogram
        );
        return rates;
    }
}
```

`BuyLabelJunction` runs after the join, and reads what both branches returned. It is the step that
spends money, and it asks the courier by the order's id, so a second try for the same order is
handed the first label.

`Shopfront.Core/Trains/FulfilOrder/Junctions/BuyLabelJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/Junctions/BuyLabelJunction.cs
using Shopfront.Couriers;
using Shopfront.Trains.FulfilOrder.Models;
using Shopfront.Warehouse;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class BuyLabelJunction(ICourier courier, ILogger<BuyLabelJunction> logger)
    : EffectJunction<(FulfilOrderInput Order, Box Box, CourierRates Rates), ShippingLabel>
{
    public override async Task<ShippingLabel> Run(
        (FulfilOrderInput Order, Box Box, CourierRates Rates) input
    )
    {
        var price = input.Rates.Base + input.Rates.PerKilogram * input.Box.Kilograms;
        var trackingNumber = await courier.BuyLabel(input.Order.OrderId, price, CancellationToken);
        logger.LogInformation("Bought label {TrackingNumber} for {Price}", trackingNumber, price);
        return new ShippingLabel(trackingNumber, price);
    }
}
```

`Shopfront.Core/Trains/FulfilOrder/Junctions/PackOrderJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/Junctions/PackOrderJunction.cs
using Shopfront.Trains.FulfilOrder.Models;
using Shopfront.Warehouse;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class PackOrderJunction(IWarehouse warehouse, ILogger<PackOrderJunction> logger)
    : EffectJunction<(FulfilOrderInput Order, ShippingLabel Label), FulfilOrderOutput>
{
    public override async Task<FulfilOrderOutput> Run(
        (FulfilOrderInput Order, ShippingLabel Label) input
    )
    {
        logger.LogInformation("Packing order {OrderId}", input.Order.OrderId);
        await warehouse.Pack(
            input.Order.OrderId,
            input.Order.Items,
            input.Label.TrackingNumber,
            CancellationToken
        );
        logger.LogInformation(
            "Packed order {OrderId} as {TrackingNumber}",
            input.Order.OrderId,
            input.Label.TrackingNumber
        );
        return new FulfilOrderOutput(input.Order.OrderId, input.Label.TrackingNumber);
    }
}
```

## The train

`Shopfront.Core/Trains/FulfilOrder/FulfilOrderTrain.cs`:

```csharp compile=app file=Shopfront.Core/Trains/FulfilOrder/FulfilOrderTrain.cs
// Parallel is experimental, and says so with TRAXEXP001 wherever it is used.
#pragma warning disable TRAXEXP001

using Shopfront.Trains.FulfilOrder.Junctions;
using Shopfront.Trains.FulfilOrder.Models;
using Trax.Core.Functional;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.FulfilOrder;

public class FulfilOrderTrain : ServiceTrain<FulfilOrderInput, FulfilOrderOutput>, IFulfilOrderTrain
{
    protected override Task<Either<Exception, FulfilOrderOutput>> Junctions() =>
        Parallel(p =>
                p.Branch("box", b => b.Chain<ChooseBoxJunction>())
                    .Branch("courier", b => b.Chain<GetCourierRatesJunction>())
            )
            .Chain<BuyLabelJunction>()
            // A fulfilment that fails after this resumes here, with the label it bought.
            .Checkpoint<ShippingLabel>()
            .Chain<PackOrderJunction>()
            .Resolve();

    // One order is fulfilled one queued run at a time, however often it is queued.
    protected override string? QueueSubjectKey(Metadata metadata) => $"order:{TrainInput.OrderId}";
}
```

What is new:

- **`Parallel`** declares a step whose **branches** run side by side, each named. A branch is
  written on the parameter it is handed, `b.Chain<...>()`, the way a track of a `Switch` is. Each
  branch starts from a copy of the run's Memory and has a dependency injection scope of its own,
  so two branches never share a database context. When both have finished, what each returned
  joins the run's Memory, and `BuyLabelJunction` reads both. The box and the rates take two
  seconds each, and the step takes two.
- **What a branch may do** is checked when the process starts, like every chain. Two branches may
  not return the same type, since the join would have two values for one type, and a branch may
  not read what a sibling returns, since it cannot see it. A branch computes and returns a value;
  anything that must be all or nothing is written after the join, because each branch commits on
  its own.
- **A failed branch** cancels the other and fails the step, with a `BranchesFailedException`
  naming each branch that failed. The failure is transient only when every branch's was.
- **`Checkpoint<ShippingLabel>()`** stores the `ShippingLabel` in Memory when the run reaches it.
  Only that value is stored, not the rest of Memory, so the startup check can tell from the chain
  alone that every step after it finds what it needs: `PackOrderJunction` needs the input, which a
  resumed run has, and the label, which the checkpoint gives back.
- **`#pragma warning disable TRAXEXP001`.** `Parallel` is experimental and reports `TRAXEXP001` as
  an error until the code that uses it says it knows. The pragma says so in the one file that does.

A run that completes deletes its checkpoints. A run that fails keeps them until a resume of it
completes or metadata cleanup takes the run.

## Fulfil an order

Start the hub and the worker, and place an order as `ada`. The worker's log, as it fulfils it:

```text
info: Shopfront.Trains.FulfilOrder.Junctions.ChooseBoxJunction[0]
      Choosing a box for order a1944ea7-d757-412e-a1b2-439ea5a1ac11
info: Shopfront.Trains.FulfilOrder.Junctions.GetCourierRatesJunction[0]
      Asking the courier's rates for order a1944ea7-d757-412e-a1b2-439ea5a1ac11
info: Shopfront.Trains.FulfilOrder.Junctions.ChooseBoxJunction[0]
      Chose a Small box for order a1944ea7-d757-412e-a1b2-439ea5a1ac11
info: Shopfront.Trains.FulfilOrder.Junctions.GetCourierRatesJunction[0]
      The courier charges 4.00 and 1.50 a kilogram
info: Shopfront.Trains.FulfilOrder.Junctions.BuyLabelJunction[0]
      Bought label TRK-A1944EA7 for 5.50
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packing order a1944ea7-d757-412e-a1b2-439ea5a1ac11
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packed order a1944ea7-d757-412e-a1b2-439ea5a1ac11 as TRK-A1944EA7
```

The box and the rates were asked for together, and the label was bought when both had answered.

## Kill the worker mid-pack, and resume

Place another order, and kill the worker while it packs, as in chapter 11:

```bash
pkill -9 -f Shopfront.Worker
```

A minute and a half later the hub has failed the run, as before. Start the worker again. As
`lin`, who is staff, find the run and ask where it can resume:

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s $G -H "Authorization: Bearer $LIN" -H 'Content-Type: application/json' \
  -d '{"query":"{ operations { executions(take: 1, trainName: \"Shopfront.Trains.FulfilOrder.IFulfilOrderTrain\", trainState: FAILED) { items { id trainState failureReason } } } }"}'
```

```json
{"data":{"operations":{"executions":{"items":[{"id":32,"trainState":"FAILED","failureReason":"Job was stuck InProgress beyond the configured stale in-progress timeout"}]}}}}
```

```bash
RUN=<the id>
curl -s $G -H "Authorization: Bearer $LIN" -H 'Content-Type: application/json' -d '{
  "query": "query($id: Long!) { operations { runGraph(metadataId: $id) { canResume allNodes { id kind state checkpointed } } } }",
  "variables": { "id": '$RUN' } }'
```

```json
{"data":{"operations":{"runGraph":{"canResume":true,"allNodes":[
  {"id":"Parallel#0","kind":"PARALLEL","state":"COMPLETED","checkpointed":false},
  {"id":"Parallel#0/box/ChooseBoxJunction#0","kind":"CHAIN","state":"COMPLETED","checkpointed":false},
  {"id":"Parallel#0/courier/GetCourierRatesJunction#0","kind":"CHAIN","state":"COMPLETED","checkpointed":false},
  {"id":"BuyLabelJunction#0","kind":"CHAIN","state":"COMPLETED","checkpointed":false},
  {"id":"Checkpoint<ShippingLabel>#0","kind":"CHECKPOINT","state":"COMPLETED","checkpointed":true},
  {"id":"PackOrderJunction#0","kind":"CHAIN","state":"INTERRUPTED","checkpointed":false},
  {"id":"Resolve#0","kind":"RESOLVE","state":"NOT_RECORDED","checkpointed":false}]}}}}
```

`runGraph` is the run drawn on its train's chain, each step with where it stands. The branches
and the label are done, the checkpoint holds the label, packing was interrupted, and `canResume`
says a resume after the checkpoint is allowed. Resume it:

```bash
curl -s $G -H "Authorization: Bearer $LIN" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: Long!) { operations { resumeExecution(id: $id) { success message id } } }",
  "variables": { "id": '$RUN' } }'
```

```json
{"data":{"operations":{"resumeExecution":{"success":true,"message":"Work queue entry 5 created.","id":5}}}}
```

The resume is queued like any run, and the worker picks it up:

```text
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packing order b1ce6f68-5f80-4b63-abcb-644536aea37e
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packed order b1ce6f68-5f80-4b63-abcb-644536aea37e as TRK-B1CE6F68
```

It neither chose a box, asked the rates nor bought a label: the run started after the checkpoint,
with the label it stored, and packed. The failed run stays failed; the resume is a run of its own,
and it completed. In the console, the failed run's page has the same as buttons: **Resume**, after its latest checkpoint, and **Resume from here** on a step.

A few things decide whether a run can resume:

- **It has to be the same chain.** The checkpoint stores a hash of the chain and the shape of the
  state. A deploy that changed either refuses the resume, with the reason, and staff requeue it
  instead, from the top.
- **Steps after the checkpoint run again** on every resume, as on any retry. `PackOrderJunction`
  packs again; a real warehouse that had half-packed a box would check for it.
- **The work before the checkpoint must have committed.** A checkpoint refuses to be stored while
  the run's Trax data context holds unsaved changes. A junction that writes, writes and saves
  before the checkpoint, so a resumed run never skips work that never happened.
- **A retry resumes too.** A manifest run that fails, the restock say, resumes after its latest
  checkpoint on its next retry, and so does a dead letter that is requeued. `requeueExecution`
  never does: it is the way to run something again from the top.

## Test it

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
