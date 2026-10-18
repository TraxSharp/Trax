---
title: "13.4 The checkout invokes and spawns"
description: "Chapter 13 of the Trax book, section 4: The checkout invokes and spawns."
type: tutorial
area: general
parent: "13. The machine places the order"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/the-machine-places-the-order/the-parcel
---

# 13.4 The checkout invokes and spawns

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

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)

---

Next: [13.5 Place an order through checkout](/docs/book/the-machine-places-the-order/place-an-order-through-checkout)
