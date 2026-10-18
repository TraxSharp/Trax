---
title: "7.3 The catalogue and the orders become queries"
description: "Chapter 7 of the Trax book, section 3: The catalogue and the orders become queries."
type: tutorial
area: general
parent: "7. Serve it over GraphQL"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/serve-it-over-graphql/placeorder-becomes-a-mutation
---

# 7.3 The catalogue and the orders become queries

An entity marked `[TraxQueryModel]` is served as a query with paging, filtering and sorting. Like a
train, it must say who may read it, or the host does not start.

The catalogue is public on purpose. Its type has a member a shopper has no use for, the row version
from chapter 4, so the query serves an interface that lists what a listing shows.

`Data/Models/IProductListing.cs`:

```csharp compile=app file=Data/Models/IProductListing.cs
namespace Shopfront.Data.Models;

// What the catalogue shows anyone who asks.
public interface IProductListing
{
    string Sku { get; }
    string Name { get; }
    decimal Price { get; }
    int Stock { get; }
}
```

`Data/Models/Product.cs`:

```csharp compile=app file=Data/Models/Product.cs
using System.ComponentModel.DataAnnotations;
using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAllowAnonymous]
[TraxQueryModel(ExposeAs = typeof(IProductListing))]
public class Product : IProductListing
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

The orders need a signed-in caller, and the query filter from chapter 6 decides which rows that
caller gets: the query model reads through the same context, so a customer's query returns their
orders and a member of staff's returns all of them. The attribute is a bare `[TraxAuthorize]`, any
signed-in caller, because the rows, not the role, are the rule.

`Data/Models/Order.cs`:

```csharp compile=app file=Data/Models/Order.cs
using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAuthorize]
[TraxQueryModel]
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

An order's items are reached through the order, and anything the schema reaches needs a posture
too, even when it is not a query of its own.

`Data/Models/OrderItem.cs`:

```csharp compile=app file=Data/Models/OrderItem.cs
using Trax.Effect.Attributes;

namespace Shopfront.Data.Models;

[TraxAuthorize]
public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
```

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [Query models](/docs/sdk-reference/graphql-api/query-models) | [Run progress](/docs/sdk-reference/graphql-api/run-progress) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch)

---

Next: [7.4 Serve the schema](/docs/book/serve-it-over-graphql/serve-the-schema)
