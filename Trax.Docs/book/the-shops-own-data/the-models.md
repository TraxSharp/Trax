---
title: "4.3 The models"
description: "Chapter 4 of the Trax book, section 3: The models."
type: tutorial
area: general
parent: "4. The shop's own data"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/the-shops-own-data/packages
---

# 4.3 The models

`Data/Models/Product.cs`:

```csharp compile=app file=Data/Models/Product.cs
using System.ComponentModel.DataAnnotations;

namespace Shopfront.Data.Models;

public class Product
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }

    // Postgres's row version. A save that finds the row changed since it was read is refused.
    [Timestamp]
    public uint Version { get; set; }
}
```

Two customers can order the last T-shirt at the same moment. Both read a stock of one, both
take one, and both save a stock of zero: one T-shirt sold twice. `Version` stops that. On Postgres a
`uint` marked `[Timestamp]` is the row's system version, `xmin`, which changes on every write.
Entity Framework adds it to the update's `WHERE`, so the second save matches no row and is refused
with a `DbUpdateConcurrencyException` instead of overwriting the first.

`Data/Models/Order.cs`:

```csharp compile=app file=Data/Models/Order.cs
namespace Shopfront.Data.Models;

public class Order
{
    public Guid Id { get; set; }
    public required string CustomerId { get; set; }
    public decimal Total { get; set; }
    public required string TransactionId { get; set; }
    public DateTime PlacedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}
```

`Data/Models/OrderItem.cs`:

```csharp compile=app file=Data/Models/OrderItem.cs
namespace Shopfront.Data.Models;

public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

An order keeps the price each item was sold at, so a later change to the catalogue does not
rewrite what a customer paid. The receipt link is not stored: the customer has it, and the shop
does not need it.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [4.4 The context and its interface](/docs/book/the-shops-own-data/the-context-and-its-interface)
