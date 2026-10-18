---
title: "12.4 States, events and context"
description: "Chapter 12 of the Trax book, section 4: States, events and context."
type: tutorial
area: general
parent: "12. Checkout is a state machine"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/checkout-is-a-state-machine/packages
---

# 12.4 States, events and context

`Shopfront.Core/Machines/Checkout/CheckoutState.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutState.cs
namespace Shopfront.Machines.Checkout;

public enum CheckoutState
{
    Cart,
    Address,
    Payment,
    Placing,
    Abandoned,
}
```

`Shopfront.Core/Machines/Checkout/CheckoutTrigger.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutTrigger.cs
namespace Shopfront.Machines.Checkout;

public enum CheckoutTrigger
{
    SetLines,
    SetAddress,
    Back,
    ChoosePayment,
    PlaceOrder,
    StartOver,
}
```

The context holds what the customer chose and nothing the server works out. There are no prices
in it: `PlaceOrder` prices the cart from the catalogue when the order is placed, so a price can
never be sent by the browser, and there is no total to fall out of step with it. The payment is
the token from chapter 3, a reference only the provider can use. A context is stored as plain JSON
and served to its owner, so a card number has no place in it.

`Shopfront.Core/Machines/Checkout/Models/CartLine.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/CartLine.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record CartLine
{
    public string Sku { get; init; } = "";
    public int Quantity { get; init; }
}
```

`Shopfront.Core/Machines/Checkout/CheckoutContext.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutContext.cs
using Shopfront.Machines.Checkout.Models;

namespace Shopfront.Machines.Checkout;

public sealed record CheckoutContext
{
    public List<CartLine>? Lines { get; init; }
    public string? Address { get; init; }
    public string? PaymentToken { get; init; }
}
```

Each event's input is a record of its own.

`Shopfront.Core/Machines/Checkout/Models/CheckoutStart.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/CheckoutStart.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record CheckoutStart;
```

`Shopfront.Core/Machines/Checkout/Models/LinesInput.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/LinesInput.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record LinesInput
{
    public List<CartLine> Lines { get; init; } = [];
}
```

`Shopfront.Core/Machines/Checkout/Models/AddressInput.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/AddressInput.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record AddressInput
{
    public string Address { get; init; } = "";
}
```

`Shopfront.Core/Machines/Checkout/Models/PaymentInput.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/Models/PaymentInput.cs
namespace Shopfront.Machines.Checkout.Models;

public sealed record PaymentInput
{
    public string PaymentToken { get; init; } = "";
}
```

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)

---

Next: [12.5 The machine](/docs/book/checkout-is-a-state-machine/the-machine)
