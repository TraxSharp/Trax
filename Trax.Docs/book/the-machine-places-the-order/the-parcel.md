---
title: "13.3 The parcel"
description: "Chapter 13 of the Trax book, section 3: The parcel."
type: tutorial
area: general
parent: "13. The machine places the order"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/the-machine-places-the-order/what-an-invoked-run-is
---

# 13.3 The parcel

Once an order is placed, the customer can follow its parcel. A parcel goes through states of its
own, so it is a machine of its own, a **child**: a state of the checkout starts it and waits for it
to end, and takes an outcome from how it ends, as it does from a run.

Our courier is a fake, as the payment provider is: a parcel is packed, in transit a minute later,
and delivered a minute after that, by timers. A real courier would move it with events.

```text
Shopfront.Core/Machines/Parcel/
├── ParcelMachine.cs
├── ParcelState.cs
├── ParcelTrigger.cs
├── ParcelContext.cs
└── Models/
    └── ParcelResult.cs
```

`Shopfront.Core/Machines/Parcel/ParcelState.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Parcel/ParcelState.cs
namespace Shopfront.Machines.Parcel;

public enum ParcelState
{
    Packed,
    InTransit,
    Delivered,
}
```

`Shopfront.Core/Machines/Parcel/ParcelTrigger.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Parcel/ParcelTrigger.cs
namespace Shopfront.Machines.Parcel;

// The fake courier moves a parcel by timers alone; a real one would send events.
public enum ParcelTrigger
{
    Scan,
}
```

`Shopfront.Core/Machines/Parcel/ParcelContext.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Parcel/ParcelContext.cs
namespace Shopfront.Machines.Parcel;

public sealed record ParcelContext;
```

`Shopfront.Core/Machines/Parcel/Models/ParcelResult.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Parcel/Models/ParcelResult.cs
namespace Shopfront.Machines.Parcel.Models;

// What the checkout is told when the parcel ends: that it ended.
public sealed record ParcelResult;
```

`Shopfront.Core/Machines/Parcel/ParcelMachine.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Parcel/ParcelMachine.cs
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Shopfront.Machines.Parcel;

public sealed class ParcelMachine : Machine<ParcelState, ParcelTrigger>
{
    protected override void Configure(IMachineBuilder<ParcelState, ParcelTrigger> m)
    {
        m.Id("parcel").Version(1).StartsAt(ParcelState.Packed, () => new JsonObject());

        m.In(ParcelState.Packed)
            .Context<ParcelContext>()
            .After(TimeSpan.FromMinutes(1))
            .GoTo(ParcelState.InTransit);
        m.In(ParcelState.InTransit)
            .Context<ParcelContext>()
            .After(TimeSpan.FromMinutes(1))
            .GoTo(ParcelState.Delivered);
        m.In(ParcelState.Delivered).Context<ParcelContext>().Ends();
    }
}
```

`Ends()` marks a state the machine ends in, which gives its parent `OnDone`. The parcel is not
served over GraphQL: only the checkout that spawned it reaches it. It belongs to the checkout's
owner, and it cannot be thrown away while the checkout waits on it.

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)

---

Next: [13.4 The checkout invokes and spawns](/docs/book/the-machine-places-the-order/the-checkout-invokes-and-spawns)
