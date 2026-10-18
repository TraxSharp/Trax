---
title: "10.3 The restock"
description: "Chapter 10 of the Trax book, section 3: The restock."
type: tutorial
area: general
parent: "10. Failures, retries and dead letters"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/failures-retries-and-dead-letters/the-supplier
---

# 10.3 The restock

The restock asks the supplier for every product at or below a threshold, and adds what arrives to
the shelves. It asks every time, even when nothing is short, which is how a feed that has gone down
is noticed.

`Trains/Restock/RestockInput.cs`:

```csharp compile=app file=Trains/Restock/RestockInput.cs
using Trax.Effect.Models.Manifest;

namespace Shopfront.Trains.Restock;

public record RestockInput : IManifestProperties
{
    public int Threshold { get; init; } = 3;
}
```

`Trains/Restock/Models/Delivery.cs`:

```csharp compile=app file=Trains/Restock/Models/Delivery.cs
namespace Shopfront.Trains.Restock.Models;

public record Delivery(string Sku, int Units);
```

`Trains/Restock/Models/ShortProducts.cs`:

```csharp compile=app file=Trains/Restock/Models/ShortProducts.cs
namespace Shopfront.Trains.Restock.Models;

internal record ShortProducts(List<string> Skus);
```

`Trains/Restock/RestockOutput.cs`:

```csharp compile=app file=Trains/Restock/RestockOutput.cs
using Shopfront.Trains.Restock.Models;

namespace Shopfront.Trains.Restock;

public record RestockOutput(List<Delivery> Delivered);
```

`Trains/Restock/IRestockTrain.cs`:

```csharp compile=app file=Trains/Restock/IRestockTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.Restock;

public interface IRestockTrain : IServiceTrain<RestockInput, RestockOutput>;
```

`Trains/Restock/Junctions/FindShortProductsJunction.cs`:

```csharp compile=app file=Trains/Restock/Junctions/FindShortProductsJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Trains.Restock.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.Restock.Junctions;

internal class FindShortProductsJunction(IShopfrontDbContext db)
    : EffectJunction<RestockInput, ShortProducts>
{
    public override async Task<ShortProducts> Run(RestockInput input) =>
        new(
            await db
                .Products.Where(p => p.Stock <= input.Threshold)
                .OrderBy(p => p.Sku)
                .Select(p => p.Sku)
                .ToListAsync(CancellationToken)
        );
}
```

`Trains/Restock/Junctions/ReceiveDeliveryJunction.cs`:

```csharp compile=app file=Trains/Restock/Junctions/ReceiveDeliveryJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Suppliers;
using Shopfront.Trains.Restock.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.Restock.Junctions;

internal class ReceiveDeliveryJunction(
    ISupplier supplier,
    IShopfrontDbContext db,
    ILogger<ReceiveDeliveryJunction> logger
) : EffectJunction<ShortProducts, RestockOutput>
{
    public override async Task<RestockOutput> Run(ShortProducts shortProducts)
    {
        var delivered = await supplier.Deliver(shortProducts.Skus, CancellationToken);

        foreach (var (sku, units) in delivered)
            await db
                .Products.Where(p => p.Sku == sku)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(p => p.Stock, p => p.Stock + units),
                    CancellationToken
                );

        logger.LogInformation("Received {Count} deliveries", delivered.Count);
        return new RestockOutput([.. delivered.Select(d => new Delivery(d.Sku, d.Units))]);
    }
}
```

`Trains/Restock/RestockTrain.cs`:

```csharp compile=app file=Trains/Restock/RestockTrain.cs
using Shopfront.Trains.Restock.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.Restock;

public class RestockTrain : ServiceTrain<RestockInput, RestockOutput>, IRestockTrain
{
    protected override Task<Either<Exception, RestockOutput>> Junctions() =>
        Chain<FindShortProductsJunction>().Chain<ReceiveDeliveryJunction>().Resolve();
}
```

A supplier that does not answer may answer next time, so its failure is transient. The classifier
says so.

`Failures/ShopfrontFailureClassifier.cs`:

```csharp compile=app file=Failures/ShopfrontFailureClassifier.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Suppliers;
using Trax.Core.Exceptions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Failures;

public class ShopfrontFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            // A message for the customer: the same order fails the same way however often it runs.
            TrainException => FailureClass.Permanent,
            // The provider did not answer this time; it may next time.
            TimeoutException => FailureClass.Transient,
            // Another order changed the stock first; trying again reads the new stock.
            DbUpdateConcurrencyException => FailureClass.Transient,
            // The supplier's feed is down; it comes back.
            SupplierUnavailableException => FailureClass.Transient,
            _ => null,
        };
}
```

The class is recorded and shown, as in chapter 3. It does not decide whether the run is retried:
a manifest's run is retried however it failed, up to its limit.

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [How retries work](/docs/scheduler/how-retries-work) | [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)

---

Next: [10.4 Tell staff when scheduled work fails](/docs/book/failures-retries-and-dead-letters/tell-staff-when-scheduled-work-fails)
