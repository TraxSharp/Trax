---
title: "1.3 The shop"
description: "Chapter 1 of the Trax book, section 3: The shop."
type: tutorial
area: general
parent: "1. An order is a train"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/an-order-is-a-train/create-the-project
---

# 1.3 The shop

Before any Trax code, the shop needs something to sell and a way to be paid. Both are plain classes.

`Inventory/Catalogue.cs`:

```csharp compile=app file=Inventory/Catalogue.cs
namespace Shopfront.Inventory;

// The catalogue and its stock, in memory for now.
public class Catalogue
{
    private readonly Dictionary<string, (decimal Price, int Stock)> items = new()
    {
        ["MUG"] = (12.00m, 10),
        ["TEE"] = (25.00m, 3),
        ["CAP"] = (18.50m, 0),
    };

    public decimal? PriceOf(string sku) => items.TryGetValue(sku, out var item) ? item.Price : null;

    // Reserves every line or none of them. Returns the first SKU that is short, or null.
    public string? TryReserve(IReadOnlyList<(string Sku, int Quantity)> lines)
    {
        lock (items)
        {
            var shortSku = lines
                .Where(l => items[l.Sku].Stock < l.Quantity)
                .Select(l => l.Sku)
                .FirstOrDefault();
            if (shortSku is not null)
                return shortSku;

            foreach (var (sku, quantity) in lines)
                items[sku] = items[sku] with { Stock = items[sku].Stock - quantity };
            return null;
        }
    }
}
```

The caps are out of stock on purpose. We will try to buy one.

`Payments/IPaymentProvider.cs`:

```csharp compile=app file=Payments/IPaymentProvider.cs
namespace Shopfront.Payments;

public interface IPaymentProvider
{
    Task<string> Charge(string customerId, decimal amount, CancellationToken cancellationToken);
}
```

`Payments/FakePaymentProvider.cs`:

```csharp compile=app file=Payments/FakePaymentProvider.cs
namespace Shopfront.Payments;

// Always succeeds. Chapter 2 makes it fail.
public class FakePaymentProvider : IPaymentProvider
{
    public Task<string> Charge(
        string customerId,
        decimal amount,
        CancellationToken cancellationToken
    ) => Task.FromResult($"tx_{Guid.NewGuid():N}"[..11]);
}
```

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)

---

Next: [1.4 What an order carries](/docs/book/an-order-is-a-train/what-an-order-carries)
