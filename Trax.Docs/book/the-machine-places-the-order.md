---
title: "13. The machine places the order"
description: "Chapter 13 of the Trax book: have checkout invoke PlaceOrder from a state, charge once however often it retries, and follow the parcel as a child machine."
type: tutorial
area: general
parent: The Trax book
nav_order: 23
snippet_context: none
follows: book/checkout-is-a-state-machine
---

# 13. The machine places the order

Checkout stops at `Placing`. In this chapter `Placing` places the order: entering it runs
`PlaceOrder` with what the checkout gathered, and the checkout moves on with the result, to
`Placed` when the order went through and to `Failed` when it did not. A failed checkout can be
tried again, and trying again never charges the customer twice. Once placed, the customer can
follow the parcel, which is a machine of its own.

This chapter starts from the end of [chapter 12](/docs/book/checkout-is-a-state-machine).

## The flow from Placing

```text
Payment ──PlaceOrder──► Placing ──(PlaceOrder done)──► Placed ──Track──► Shipping ──(parcel ends)──► Delivered
                           │  ▲
               (run failed)│  │Retry
                           ▼  │
                          Failed
```

Two kinds of arrow are new. `(PlaceOrder done)` and `(run failed)` are **outcomes**: nobody sends
them; the run that `Placing` started ends, and its ending moves the checkout. `(parcel ends)` is
the same for a child machine. Every other arrow is an event a customer sends.

## What an invoked run is

When a checkout enters `Placing`, the server queues a run of `PlaceOrder` with an input it builds
from the checkout's context, and remembers the run. The run goes through the queue like any queued
work, so in Shopfront the worker runs it. When it ends, the hub applies its outcome to the checkout
that started it, whether or not anyone is watching. A late result from a run the checkout has
moved on from is refused.

Three things about Shopfront have to change for that to work, and each is a rule worth knowing.

### An invoked train's output holds no secret

The run's output is written into the checkout's context, which is stored as plain JSON and served
to its owner. `[TraxSensitive]` masks a value in the copies Trax keeps of a run; it cannot mask one
the train hands on to be stored. So the host refuses to start when an invoked train's output
reaches a `[TraxSensitive]` member, and `PlaceOrderOutput` has one: the receipt link. It leaves the
output. The payment's own record still carries it, masked, and the provider sends it to the
customer, as providers do.

`Shopfront.Core/Trains/PlaceOrder/PlaceOrderOutput.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/PlaceOrderOutput.cs
namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderOutput(Guid OrderId, decimal Total, string TransactionId);
```

`Shopfront.Core/Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Data;
using Shopfront.Data.Models;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(IShopfrontDbContext db, ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(Customer Customer, PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override async Task<PlaceOrderOutput> Run(
        (Customer Customer, PricedCart Cart, Payment Payment) input
    )
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = input.Customer.Id,
            Total = input.Cart.Total,
            TransactionId = input.Payment.TransactionId,
            PlacedAt = DateTime.UtcNow,
            Items =
            [
                .. input.Cart.Lines.Select(l => new OrderItem
                {
                    Sku = l.Sku,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                }),
            ],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(CancellationToken);

        logger.LogInformation("Confirmed order {OrderId}", order.Id);
        return new PlaceOrderOutput(order.Id, order.Total, order.TransactionId);
    }
}
```

### The customer is the checkout's owner

`PlaceOrder` has asked who the caller is since chapter 6. An invoked run has no caller: it runs on
the worker, where nobody is signed in, and `TraxPrincipal` cannot be resolved there. What it has is
the checkout it was invoked by, and the checkout's owner. Trax hands the run an `InvokedRun` naming
both, which can only be resolved inside a run a machine invoked.

A small helper finds it, or nothing.

`Shopfront.Core/Machines/InvokedRuns.cs`:

```csharp compile=app file=Shopfront.Core/Machines/InvokedRuns.cs
using Trax.Effect.StateMachine.Persistence;

namespace Shopfront.Machines;

public static class InvokedRuns
{
    // The machine instance that invoked the current run, or null in a run no machine invoked.
    public static InvokedRun? Current(IServiceProvider services)
    {
        try
        {
            return services.GetService<InvokedRun>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
```

The caller the shop's code sees falls back to the owner. A user key is the same qualified id a
signed-in caller has, `TraxJwt:ada`, so the order is hers either way, and the fraud check and the
query filter see her orders.

`Shopfront.Core/Auth/TraxShopfrontCaller.cs`:

```csharp compile=app file=Shopfront.Core/Auth/TraxShopfrontCaller.cs
using Shopfront.Data;
using Shopfront.Machines;
using Trax.Api.Auth;

namespace Shopfront.Auth;

// The signed-in caller, or, in a run a checkout invoked, the checkout's owner.
public sealed class TraxShopfrontCaller(TraxCaller caller, IServiceProvider services)
    : IShopfrontCaller
{
    public string? CustomerId => caller.Principal?.Id ?? InvokedRuns.Current(services)?.UserKey;
    public bool IsStaff => caller.Principal?.Roles.Contains(Roles.Staff) == true;
}
```

`IdentifyCustomerJunction` asks the shop's caller, not `TraxPrincipal`, so it works on both paths.
With neither a caller nor an owner it refuses, rather than placing an order for nobody.

`Shopfront.Core/Trains/PlaceOrder/Junctions/IdentifyCustomerJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/Junctions/IdentifyCustomerJunction.cs
using Shopfront.Data;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class IdentifyCustomerJunction(IShopfrontCaller caller)
    : EffectJunction<PlaceOrderInput, Customer>
{
    public override Task<Customer> Run(PlaceOrderInput input) =>
        Task.FromResult(
            new Customer(
                caller.CustomerId ?? throw new TrainException("Sign in to place an order.")
            )
        );
}
```

### A retry never charges twice

A failed checkout can be retried, and each try is a new run of `PlaceOrder` from its first
junction. If a try charged the card and then failed, storing the order say, the next try would
charge it again. A payment provider prevents that with an **idempotency key**: a charge sent with a
key it has seen is answered with the first charge, not made again. The key has to stay the same
across every try of one order, so it is the checkout's id.

`Shopfront.Core/Payments/IPaymentProvider.cs`:

```csharp compile=app file=Shopfront.Core/Payments/IPaymentProvider.cs
namespace Shopfront.Payments;

public interface IPaymentProvider
{
    // A charge with an idempotency key the provider has seen is answered with that charge, not
    // made again.
    Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        string? idempotencyKey,
        CancellationToken cancellationToken
    );
}
```

`Shopfront.Core/Payments/FakePaymentProvider.cs`:

```csharp compile=app file=Shopfront.Core/Payments/FakePaymentProvider.cs
using System.Collections.Concurrent;

namespace Shopfront.Payments;

public class FakePaymentProvider : IPaymentProvider
{
    public const string DeclinedToken = "tok_chargeDeclined";

    private readonly ConcurrentDictionary<string, ChargeResult> byKey = new();
    private int charges;

    public Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        string? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        if (idempotencyKey is not null && byKey.TryGetValue(idempotencyKey, out var earlier))
            return Task.FromResult(earlier);

        if (paymentToken == DeclinedToken)
            throw new PaymentDeclinedException("The card was declined.");

        if (Interlocked.Increment(ref charges) % 5 == 0)
            throw new TimeoutException("The payment provider did not answer.");

        var transactionId = $"tx_{Guid.NewGuid():N}"[..11];
        var charge = new ChargeResult(
            transactionId,
            $"https://pay.example/receipts/{transactionId}?key={Guid.NewGuid():N}"
        );
        if (idempotencyKey is not null)
            byKey[idempotencyKey] = charge;
        return Task.FromResult(charge);
    }
}
```

`Shopfront.Core/Trains/PlaceOrder/Junctions/TakePaymentJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/Junctions/TakePaymentJunction.cs
using Shopfront.Inventory;
using Shopfront.Machines;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class TakePaymentJunction(
    IPaymentProvider payments,
    Catalogue catalogue,
    IServiceProvider services,
    ILogger<TakePaymentJunction> logger
)
    : EffectJunction<
        (Customer Customer, PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation),
        Payment
    >
{
    public override async Task<Payment> Run(
        (
            Customer Customer,
            PlaceOrderInput Order,
            PricedCart Cart,
            StockReservation Reservation
        ) input
    )
    {
        // Every try of one checkout's order carries the same key, so a retry is never charged twice.
        var idempotencyKey = InvokedRuns.Current(services) is { } checkout
            ? $"checkout:{checkout.InstanceId}"
            : null;

        ChargeResult charge;
        try
        {
            charge = await payments.Charge(
                input.Customer.Id,
                input.Order.PaymentToken,
                input.Cart.Total,
                idempotencyKey,
                CancellationToken
            );
        }
        catch (Exception ex)
        {
            // However the charge failed, the order stops here, so its stock goes back, even when
            // the run itself was cancelled.
            await catalogue.Release(
                [.. input.Cart.Lines.Select(l => (l.Sku, l.Quantity))],
                CancellationToken.None
            );
            logger.LogInformation("Released reservation {ReservationId}", input.Reservation.Id);

            if (ex is PaymentDeclinedException)
                throw new TrainException(ex.Message);
            throw;
        }

        logger.LogInformation(
            "Charged {Total} as {TransactionId}",
            input.Cart.Total,
            charge.TransactionId
        );
        return new Payment(charge.TransactionId, input.Cart.Total, charge.ReceiptUrl);
    }
}
```

An order placed over the `placeOrder` mutation has no checkout, so it is charged without a key, as
before.

## The parcel

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

## The checkout invokes and spawns

A checkout keeps what it gathered after the order is placed, and each later state adds to it: a
placed checkout holds the order the run created as well, and a shipping one the parcel it is
waiting on. A state's context lists every field it holds, so each repeats the ones before it.

`Shopfront.Core/Machines/Checkout/PlacedContext.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/PlacedContext.cs
using Shopfront.Machines.Checkout.Models;

namespace Shopfront.Machines.Checkout;

public sealed record PlacedContext
{
    public List<CartLine>? Lines { get; init; }
    public string? Address { get; init; }
    public string? PaymentToken { get; init; }
    public Guid? OrderId { get; init; }
}
```

`Shopfront.Core/Machines/Checkout/ShippingContext.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/ShippingContext.cs
using Shopfront.Machines.Checkout.Models;

namespace Shopfront.Machines.Checkout;

public sealed record ShippingContext
{
    public List<CartLine>? Lines { get; init; }
    public string? Address { get; init; }
    public string? PaymentToken { get; init; }
    public Guid? OrderId { get; init; }
    public Guid? ParcelId { get; init; }
}
```

`Shopfront.Core/Machines/Checkout/CheckoutState.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutState.cs
namespace Shopfront.Machines.Checkout;

public enum CheckoutState
{
    Cart,
    Address,
    Payment,
    Placing,
    Placed,
    Failed,
    Shipping,
    Delivered,
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
    Retry,
    Track,
    StartOver,
}
```

`Shopfront.Core/Machines/Checkout/CheckoutMachine.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutMachine.cs
using System.Text.Json.Nodes;
using Shopfront.Auth;
using Shopfront.Machines.Checkout.Models;
using Shopfront.Machines.Parcel;
using Shopfront.Machines.Parcel.Models;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Shopfront.Machines.Checkout;

[TraxMachine]
public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    private static readonly Rule HasLines = Field((CheckoutContext c) => c.Lines).CountAtLeast(1);

    private static readonly Lazy<string?> EmbeddedCorpus = new(() =>
    {
        using var stream = typeof(CheckoutMachine).Assembly.GetManifestResourceStream(
            "machines/checkout/differential.json"
        );
        return stream is null ? null : new StreamReader(stream).ReadToEnd();
    });

    // The committed corpus, embedded in the library, so the machine can replay it and check itself.
    public override string? Corpus => EmbeddedCorpus.Value;

    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout")
            .Version(1)
            .StartsAt(CheckoutState.Cart, () => new JsonObject())
            .Start<CheckoutStart>(start => start.Reduce(Keep()).Requires(Roles.Customer))
            .Get(read => read.OwnerOnly())
            .Watch(read => read.OwnerOnly())
            .Discard(read => read.OwnerOnly());

        m.In(CheckoutState.Cart)
            .Context<CheckoutContext>()
            .After(TimeSpan.FromHours(2))
            .GoTo(CheckoutState.Abandoned)
            .On(CheckoutTrigger.SetLines)
            .AnyAuthenticated()
            .WithInput<LinesInput>()
            .Reduce(Set((CheckoutContext c) => c.Lines).FromInput((LinesInput i) => i.Lines))
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.SetAddress)
            .AnyAuthenticated()
            .WithInput<AddressInput>()
            .When(All(HasLines, Input((AddressInput i) => i.Address).NonEmpty()))
            .Because("Put something in the cart and give an address to send it to.")
            .Reduce(Set((CheckoutContext c) => c.Address).FromInput((AddressInput i) => i.Address))
            .To(CheckoutState.Address);

        m.In(CheckoutState.Address)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Back)
            .AnyAuthenticated()
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.ChoosePayment)
            .AnyAuthenticated()
            .WithInput<PaymentInput>()
            .When(Input((PaymentInput i) => i.PaymentToken).NonEmpty())
            .Because("Choose how to pay.")
            .Reduce(
                Set((CheckoutContext c) => c.PaymentToken)
                    .FromInput((PaymentInput i) => i.PaymentToken)
            )
            .To(CheckoutState.Payment);

        m.In(CheckoutState.Payment)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Back)
            .AnyAuthenticated()
            .To(CheckoutState.Address)
            .On(CheckoutTrigger.PlaceOrder)
            .Requires(Roles.Customer)
            .To(CheckoutState.Placing);

        // Entering Placing runs PlaceOrder with what the checkout gathered, and its end moves the checkout.
        m.In(CheckoutState.Placing)
            .Context<CheckoutContext>()
            .Invokes<IPlaceOrderTrain, PlaceOrderInput, PlaceOrderOutput>(OrderFor)
            .OnDone(
                CheckoutState.Placed,
                reduce: Set((PlacedContext c) => c.OrderId)
                    .FromInput((PlaceOrderOutput o) => o.OrderId)
            )
            .OnFailed(CheckoutState.Failed)
            .OnCancelled(CheckoutState.Failed);

        m.In(CheckoutState.Failed)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.Retry)
            .Requires(Roles.Customer)
            .To(CheckoutState.Placing);

        m.In(CheckoutState.Placed)
            .Context<PlacedContext>()
            .On(CheckoutTrigger.Track)
            .AnyAuthenticated()
            .To(CheckoutState.Shipping);

        // Entering Shipping starts the parcel's machine, and its end moves the checkout.
        m.In(CheckoutState.Shipping)
            .Context<ShippingContext>()
            .Spawns<ParcelMachine, ShippingContext, ParcelResult>(c => c.ParcelId)
            .OnDone(CheckoutState.Delivered)
            .OnFailed(CheckoutState.Placed)
            .OnCancelled(CheckoutState.Placed);

        m.In(CheckoutState.Delivered).Context<ShippingContext>();

        m.In(CheckoutState.Abandoned)
            .Context<CheckoutContext>()
            .On(CheckoutTrigger.StartOver)
            .AnyAuthenticated()
            .Reduce(Reset())
            .To(CheckoutState.Cart);

        m.Differential(d =>
            d.Sample(
                    CheckoutTrigger.SetLines,
                    new LinesInput { Lines = [new CartLine { Sku = "MUG", Quantity = 2 }] }
                )
                .Sample(CheckoutTrigger.SetAddress, new AddressInput { Address = "1 Main St" })
                .Sample(CheckoutTrigger.SetAddress, new AddressInput())
                .Sample(
                    CheckoutTrigger.ChoosePayment,
                    new PaymentInput { PaymentToken = "tok_visa" }
                )
                .EmptySample(CheckoutTrigger.ChoosePayment)
                .OutcomeSample(
                    CheckoutState.Placing,
                    new PlaceOrderOutput(
                        Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89"),
                        24.00m,
                        "tx_sample"
                    )
                )
        );
    }

    // The order the checkout places: its lines and its payment token. The train prices it.
    private static PlaceOrderInput OrderFor(JsonObject checkout) =>
        new()
        {
            Lines =
            [
                .. checkout["lines"]!
                    .AsArray()
                    .Select(line => new OrderLine(
                        line!["sku"]!.GetValue<string>(),
                        line["quantity"]!.GetValue<int>()
                    )),
            ],
            PaymentToken = checkout["paymentToken"]!.GetValue<string>(),
        };
}
```

What is new:

- **`Invokes<IPlaceOrderTrain, PlaceOrderInput, PlaceOrderOutput>(OrderFor)`** runs `PlaceOrder`
  every time the checkout enters `Placing`. `OrderFor` builds its input from the context, on the
  server, which reads the context as the JSON it is stored as, in camelCase.
- **`OnDone`, `OnFailed`, `OnCancelled`** are where each ending goes. `OnDone` also writes the order's
  id into `Placed`'s context, from the run's output. Only an outcome can enter these states; an
  event that tried would be refused when the machine is built.
- **The roles.** `PlaceOrder` admits only customers. Every event that enters `Placing`, here
  `PlaceOrder` and `Retry`, must require at least that, or the host refuses to start: a checkout may
  never place an order its owner could not place themselves. When the event is sent, the sender is
  checked against the train's `[TraxAuthorize]` too.
- **`Retry`** is how a failed checkout is tried again. Trax never retries an invoked run on its own,
  because the checkout, not the scheduler, decides what happens next.
- **`Track`** is an event, not an outcome: an outcome may not enter a state that starts a run or a
  child, so the customer asks to follow the parcel. **`Spawns<ParcelMachine, ...>(c => c.ParcelId)`**
  starts the parcel on entry, writes its id into the context, and waits for it to end.

The worker runs the invoked `PlaceOrder`, and a run a machine waits on has to be stoppable between
junctions. The worker gets junction progress for that.

`Shopfront.Worker/Shopfront.Worker.csproj`:

```xml file=Shopfront.Worker/Shopfront.Worker.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Shopfront</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Shopfront.Core\Shopfront.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Progress" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Scheduler" />
  </ItemGroup>
</Project>
```

`Shopfront.Worker/Program.cs`:

```csharp compile=worker file=Shopfront.Worker/Program.cs
using Shopfront;
using Shopfront.Extensions;
using Shopfront.Staff;
using Trax.Api.Auth;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddShopfront(connectionString);

// The trains ask who the caller is. On the worker nobody is, and that is the answer they get.
builder.Services.AddTraxPrincipalAccessor();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddJunctionProgress()
                .AddDecisionRecording()
                .AddJunctionEvents()
                .AddLifecycleHook<StaffFailureHook>()
        )
        .AddMediator(mediator =>
            mediator
                .ScanAssemblies(typeof(ManifestNames).Assembly)
                // The hub authorized every job this worker runs before it was queued.
                .AllowMissingAuthorizationService()
        )
);
builder.Services.AddTraxWorker(worker => worker.WorkerCount = 2);

var app = builder.Build();
await app.RunAsync();
```

The hub needs nothing new: it already finds every machine in the library, the parcel included,
and it is the hub that applies each outcome when the worker's run ends.

Regenerate the definition and corpus, since the machine changed, and build again so the library
embeds them:

```bash
dotnet build Shopfront.Core
dotnet trax machine generate --assembly Shopfront.Core/bin/Debug/net10.0/Shopfront.Core.dll \
  --machine Shopfront.Machines.Checkout.CheckoutMachine \
  --ir-out machines/checkout --corpus-out machines/checkout
dotnet build
```

## Place an order through checkout

Start the hub and the worker. As `ada`, start a checkout and take it to `Payment` as in chapter 12.
This chapter's `send` asks for the run a write started:

```bash
send() { curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "mutation($id: UUID!) { machines { checkout { '"$1"' { state invokedRun { runId } problem { code message } } } } }",
  "variables": { "id": "'$ID'" } }'; echo; }
get() { curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' -d '{
  "query": "query($id: UUID!) { machines { checkout { get(id: $id) { state context { orderId parcelId } } } } }",
  "variables": { "id": "'$ID'" } }'; echo; }
send 'placeOrder(id: $id, requestId: \"r-6\")'
```

```json
{"data":{"machines":{"checkout":{"placeOrder":{"state":"Placing","invokedRun":{"runId":"db37bfbe148543dc9c9287b419ba4077"},"problem":null}}}}}
```

The checkout is in `Placing`, and `invokedRun` names the run it queued. A few seconds later the
worker has run it, the hub has applied its outcome, and `get` says so:

```bash
get
```

```json
{"data":{"machines":{"checkout":{"get":{"state":"Placed","context":{"orderId":"04fcc4d8-0d94-4b2f-92b9-95b8c52755cf","parcelId":null}}}}}}
```

The order is `ada`'s, because the invoked run knew whose checkout it was. Her orders over GraphQL:

```bash
curl -s $G -H "Authorization: Bearer $ADA" -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { orders { nodes { id customerId total } } } }"}'
```

```json
{"data":{"discover":{"orders":{"nodes":[{"id":"04fcc4d8-0d94-4b2f-92b9-95b8c52755cf","customerId":"TraxJwt:ada","total":24.00}]}}}}
```

Ask to follow the parcel:

```bash
send 'track(id: $id, requestId: \"r-7\")'
get
```

```json
{"data":{"machines":{"checkout":{"track":{"state":"Shipping","invokedRun":{"runId":"6b3326b5f2e44e7896a7d97a26fbccbd"},"problem":null}}}}}
{"data":{"machines":{"checkout":{"get":{"state":"Shipping","context":{"orderId":"04fcc4d8-0d94-4b2f-92b9-95b8c52755cf","parcelId":"e4bd886a-28e0-4938-9d11-ffe1b22b2b22"}}}}}}
```

`parcelId` is the parcel the checkout started, written by the server. Two minutes later the
parcel's timers have taken it to `Delivered`, which ends it, and its end has moved the checkout:

```bash
get
```

```json
{"data":{"machines":{"checkout":{"get":{"state":"Delivered","context":{"orderId":"04fcc4d8-0d94-4b2f-92b9-95b8c52755cf","parcelId":"e4bd886a-28e0-4938-9d11-ffe1b22b2b22"}}}}}}
```

## When the order fails

Start a second checkout, take it to `Payment` with `tok_chargeDeclined`, and place the order:

```bash
send 'placeOrder(id: $id, requestId: \"d-6\")'
get
```

```json
{"data":{"machines":{"checkout":{"placeOrder":{"state":"Placing","invokedRun":{"runId":"68617bc0e1cb4059948881b0f8479b68"},"problem":null}}}}}
{"data":{"machines":{"checkout":{"get":{"state":"Failed","context":{"orderId":null,"parcelId":null}}}}}}
```

The run failed at the payment, and the checkout took `OnFailed`. `Retry` runs `PlaceOrder` again
from its first junction, as a new run:

```bash
send 'retry(id: $id, requestId: \"d-7\")'
```

```json
{"data":{"machines":{"checkout":{"retry":{"state":"Placing","invokedRun":{"runId":"c3aab85fdb7d43f3939ea05d3acad073"},"problem":null}}}}}
```

With a card that declines, it fails again and the checkout is back in `Failed`. A try that was
charged and then failed, which the fake cannot do but a real system can, is answered from the
first charge on the next try: the customer is charged once however often they retry.

A worker that stops in the middle of an invoked run has its run failed, when the run is found
stale or the scheduler next starts, and the checkout takes `OnFailed` in the same way. A
checkpoint does not resume it. The customer retries, and the key keeps the retry from charging
twice.

## Test it

The payment junction's tests gain the key, and one more: a charge with a key it has seen is not
made again.

`tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class TakePaymentJunctionTests
{
    private sealed class ScriptedPaymentProvider(Exception? failWith = null) : IPaymentProvider
    {
        public List<(
            string CustomerId,
            string PaymentToken,
            decimal Amount,
            string? Key
        )> Charges { get; } = [];

        public Task<ChargeResult> Charge(
            string customerId,
            string paymentToken,
            decimal amount,
            string? idempotencyKey,
            CancellationToken cancellationToken
        )
        {
            Charges.Add((customerId, paymentToken, amount, idempotencyKey));
            return failWith is null
                ? Task.FromResult(
                    new ChargeResult("tx_test", "https://pay.example/receipts/tx_test")
                )
                : Task.FromException<ChargeResult>(failWith);
        }
    }

    private static readonly Customer Ada = new("TraxJwt:ada");

    private static readonly PlaceOrderInput Order = new()
    {
        Lines = [new OrderLine("TEE", 3)],
        PaymentToken = "tok_visa",
    };

    private static readonly PricedCart Cart = new([new PricedLine("TEE", 3, 25.00m)], 75.00m);

    // Resets the database and reserves the three T-shirts, as ReserveStockJunction leaves them.
    [SetUp]
    public async Task ReserveTheTShirts()
    {
        await TestDatabase.Reset();
        await using var db = TestDatabase.NewContext();
        (await new Catalogue(db).TryReserve([("TEE", 3)], CancellationToken.None))
            .Should()
            .BeNull();
    }

    private static async Task<Payment> Run(IPaymentProvider payments)
    {
        await using var db = TestDatabase.NewContext();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var junction = new TakePaymentJunction(
            payments,
            new Catalogue(db),
            services,
            NullLogger<TakePaymentJunction>.Instance
        );
        return await junction.Run((Ada, Order, Cart, new StockReservation(Guid.NewGuid())));
    }

    [Test]
    public async Task Run_NotInvokedByACheckout_ChargesWithoutAKey()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Run(payments);

        payments.Charges.Should().Equal(("TraxJwt:ada", "tok_visa", 75.00m, (string?)null));
        payment
            .Should()
            .Be(new Payment("tx_test", 75.00m, "https://pay.example/receipts/tx_test"));
        (await TestDatabase.StockOf("TEE")).Should().Be(0);
    }

    [Test]
    public async Task Run_Declined_ThrowsTheReasonAndReleasesTheStock()
    {
        var act = () =>
            Run(
                new ScriptedPaymentProvider(new PaymentDeclinedException("The card was declined."))
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        (await TestDatabase.StockOf("TEE")).Should().Be(3, "the three T-shirts are back");
    }

    [Test]
    public async Task Run_TimedOut_RethrowsAndReleasesTheStock()
    {
        var act = () => Run(new ScriptedPaymentProvider(new TimeoutException("No answer.")));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        (await TestDatabase.StockOf("TEE")).Should().Be(3, "the three T-shirts are back");
    }

    [Test]
    public async Task FakeProvider_SameKey_AnswersTheFirstChargeAgain()
    {
        var provider = new FakePaymentProvider();

        var first = await provider.Charge(
            "TraxJwt:ada",
            "tok_visa",
            75.00m,
            "checkout:1",
            CancellationToken.None
        );
        var again = await provider.Charge(
            "TraxJwt:ada",
            "tok_visa",
            75.00m,
            "checkout:1",
            CancellationToken.None
        );

        again.Should().Be(first, "a retry with the same key is not charged again");
    }
}
```

`tests/Shopfront.Tests/UnitTests/ConfirmOrderJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ConfirmOrderJunctionTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ConfirmOrderJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    [Test]
    public async Task Run_Paid_StoresTheOrderForTheCaller()
    {
        var cart = new PricedCart([new PricedLine("MUG", 2, 12.00m)], 24.00m);
        var payment = new Payment("tx_test", 24.00m, "https://pay.example/receipts/tx_test");

        await using (var db = TestDatabase.NewContext())
        {
            var output = await new ConfirmOrderJunction(
                db,
                NullLogger<ConfirmOrderJunction>.Instance
            ).Run((new Customer("TraxJwt:ada"), cart, payment));

            output.TransactionId.Should().Be("tx_test");
        }

        await using var read = TestDatabase.NewContext();
        var order = await read.Orders.Include(o => o.Items).SingleAsync();
        order.CustomerId.Should().Be("TraxJwt:ada");
        order.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(12.00m);
    }
}
```

The train test no longer looks for the receipt link in the output, which no longer has one.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Fraud;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Api.Auth;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class PlaceOrderTrainTests
{
    private const string Ada = "TraxJwt:ada";

    private ServiceProvider _provider = null!;

    private async Task Build(Action<IServiceCollection>? replace = null)
    {
        await TestDatabase.Reset();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShopfront(TestDatabase.ConnectionString);
        // The caller a signed-in request would have given the run.
        services.AddScoped(_ => new TraxPrincipal(Ada, "ada", [Roles.Customer]));
        services.AddScoped<IShopfrontCaller>(_ => TestCaller.Customer(Ada));
        replace?.Invoke(services);
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    // A payment token no other test uses, to find this run's record among any others.
    private static string NewToken() => $"tok_{Guid.NewGuid():N}";

    private static PlaceOrderInput Order(string paymentToken, string sku, int quantity = 1) =>
        new() { Lines = [new OrderLine(sku, quantity)], PaymentToken = paymentToken };

    [Test]
    public async Task RunAsync_InStock_StoresTheOrderForTheCaller()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG"));

        var run = RunFor(token);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.SingleAsync()).CustomerId.Should().Be(Ada);
        (await TestDatabase.StockOf("MUG")).Should().Be(9);
    }

    [Test]
    public async Task RunAsync_LargeFirstOrder_IsHeldBeforeStockOrPayment()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG", 9));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*You have not been charged.");
        RunFor(token).FailureJunction.Should().Be("RefuseOrderJunction");
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_DeciderSaysYes_HoldsEvenASmallOrder()
    {
        var decider = new ScriptedDecider().YesNo<LooksFraudulent>(0.9);
        await Build(services => services.AddSingleton<IDecider>(decider));
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(NewToken(), "MUG"));

        await act.Should().ThrowAsync<TrainException>();
        decider
            .Requests.Should()
            .ContainSingle()
            .Which.State.Should()
            .Be(new OrderRisk(Ada, 12.00m, FirstOrder: true));
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAsPermanent()
    {
        await Build();
        var token = NewToken();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "CAP"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        var run = RunFor(token);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureJunction.Should().Be("ReserveStockJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task RunAsync_Declined_FailsAtTakePaymentAndStoresNoOrder()
    {
        await Build();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunAsync<PlaceOrderOutput>(Order(FakePaymentProvider.DeclinedToken, "MUG"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        var run = RunFor(FakePaymentProvider.DeclinedToken);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.AnyAsync()).Should().BeFalse();
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_FifthCharge_TimesOutAsTransient()
    {
        await Build();
        var bus = _provider.GetRequiredService<ITrainBus>();
        for (var i = 0; i < 4; i++)
            await bus.RunAsync<PlaceOrderOutput>(Order(NewToken(), "MUG"));
        var token = NewToken();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(token, "MUG"));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        var run = RunFor(token);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Transient);
    }

    private Metadata RunFor(string paymentToken)
    {
        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return data
            .Metadatas.Where(m => m.Name == typeof(IPlaceOrderTrain).FullName)
            .AsEnumerable()
            .Single(m => m.Input?.Contains(paymentToken) == true);
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    71, Skipped:     0, Total:    71 - Shopfront.Tests.dll (net10.0)
```

## What we have

Checkout places the order itself: entering `Placing` runs `PlaceOrder` on the worker, as the
checkout's owner, and the run's end moves the checkout, to `Placed` with the order's id or to
`Failed`, from where the customer retries without being charged twice. A placed order's parcel is
a machine of its own that the checkout waits on.

That ends the part on machines. The next part goes back to fulfilment, to run its checks side by
side and to pick it up where it stopped.

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)
