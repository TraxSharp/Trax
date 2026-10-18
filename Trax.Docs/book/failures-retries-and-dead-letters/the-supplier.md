---
title: "10.2 The supplier"
description: "Chapter 10 of the Trax book, section 2: The supplier."
type: tutorial
area: general
parent: "10. Failures, retries and dead letters"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/failures-retries-and-dead-letters/what-is-retried
---

# 10.2 The supplier

The supplier delivers stock for the products it is asked about. Ours is a fake that a developer can
take down and bring back.

`Suppliers/SupplierUnavailableException.cs`:

```csharp compile=app file=Suppliers/SupplierUnavailableException.cs
namespace Shopfront.Suppliers;

public class SupplierUnavailableException(string message) : Exception(message);
```

`Suppliers/ISupplier.cs`:

```csharp compile=app file=Suppliers/ISupplier.cs
namespace Shopfront.Suppliers;

public interface ISupplier
{
    // Delivers what the supplier can of each SKU: a list of (SKU, units) actually sent.
    Task<IReadOnlyList<(string Sku, int Units)>> Deliver(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken
    );
}
```

`Suppliers/FakeSupplier.cs`:

```csharp compile=app file=Suppliers/FakeSupplier.cs
namespace Shopfront.Suppliers;

// Sends a carton of five of anything asked for, unless it has been taken down.
public class FakeSupplier : ISupplier
{
    public const int Carton = 5;

    public bool IsDown { get; set; }

    public Task<IReadOnlyList<(string Sku, int Units)>> Deliver(
        IReadOnlyList<string> skus,
        CancellationToken cancellationToken
    )
    {
        if (IsDown)
            throw new SupplierUnavailableException("The supplier's feed did not answer.");
        return Task.FromResult<IReadOnlyList<(string Sku, int Units)>>(
            [.. skus.Select(sku => (sku, Carton))]
        );
    }
}
```

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [How retries work](/docs/scheduler/how-retries-work) | [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)

---

Next: [10.3 The restock](/docs/book/failures-retries-and-dead-letters/the-restock)
