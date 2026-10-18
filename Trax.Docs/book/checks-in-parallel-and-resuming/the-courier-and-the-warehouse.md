---
title: "14.2 The courier and the warehouse"
description: "Chapter 14 of the Trax book, section 2: The courier and the warehouse."
type: tutorial
area: general
parent: "14. Checks in parallel, and resuming"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/checks-in-parallel-and-resuming/the-new-chain
---

# 14.2 The courier and the warehouse

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

## SDK Reference

> [Parallel](/docs/sdk-reference/train-methods/parallel) | [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | [Checkpoints and branches](/docs/core/checkpoints-and-branches) | [resumeExecution](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) | [runGraph](/docs/sdk-reference/graphql-api/queries/executions#rungraph)

---

Next: [14.3 The train](/docs/book/checks-in-parallel-and-resuming/the-train)
