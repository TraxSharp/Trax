---
title: "11.3 Fulfilment is a train"
description: "Chapter 11 of the Trax book, section 3: Fulfilment is a train."
type: tutorial
area: general
parent: "11. A separate worker"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/a-separate-worker/the-projects
---

# 11.3 Fulfilment is a train

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

## SDK Reference

> [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Queued work](/docs/mediator/queued-work) | [Job submission](/docs/scheduler/job-submission) | [Structuring your application](/docs/reference/project-layout) | [Host tracking](/docs/effect/host-tracking)

---

Next: [11.4 PlaceOrder queues it](/docs/book/a-separate-worker/placeorder-queues-it)
