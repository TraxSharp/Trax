---
title: "12.5 The machine"
description: "Chapter 12 of the Trax book, section 5: The machine."
type: tutorial
area: general
parent: "12. Checkout is a state machine"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/checkout-is-a-state-machine/states-events-and-context
---

# 12.5 The machine

`Shopfront.Core/Machines/Checkout/CheckoutMachine.cs`:

```csharp compile=app file=Shopfront.Core/Machines/Checkout/CheckoutMachine.cs
using System.Text.Json.Nodes;
using Shopfront.Auth;
using Shopfront.Machines.Checkout.Models;
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
                Set((CheckoutContext c) => c.PaymentToken).FromInput((PaymentInput i) => i.PaymentToken)
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

        m.In(CheckoutState.Placing).Context<CheckoutContext>();

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
                .Sample(CheckoutTrigger.ChoosePayment, new PaymentInput { PaymentToken = "tok_visa" })
                .EmptySample(CheckoutTrigger.ChoosePayment)
        );
    }
}
```

From the top:

- **`Id("checkout")`** names the machine. It names the GraphQL field, the stored instances and every
  file generated from it. **`Version(1)`** is stamped on every stored instance. A change that leaves
  stored instances unreadable, such as a context field renamed, bumps it, and says with
  `MigrateFrom` how to read the old ones.
- **`Start<CheckoutStart>`** says who may start a checkout: a customer. A started checkout belongs to
  whoever started it.
- **`Get`, `Watch` and `Discard`** say who may read it, follow it and throw it away: its owner and
  nobody else. The host refuses to serve a machine that leaves any of them, or any event, without
  a rule, and refuses one looser than the owner for the three reads.
- **`In(state).Context<T>()`** opens a state and says what its context holds.
- **`After(TimeSpan.FromHours(2)).GoTo(...)`** is a timer. A cart left alone for two hours moves to
  `Abandoned`. Every entry to `Cart` restarts it, so each `SetLines` buys the customer another two
  hours. The server fires it, with or without anyone watching.
- **`On(trigger)`** opens an event, and what follows is its rule, its input, its guard, its
  reduction and its target:
  - **`AnyAuthenticated()`** or **`Requires(Roles.Customer)`** is who may send it. Only placing the
    order needs the customer role. Every state a role-gated event enters is reserved, kept from
    being discarded or expiring, so the cart's own events need only a signed-in owner.
  - **`When(rule).Because(text)`** is a guard: the event is refused, with that text, unless the
    rule holds. Rules are data, `Field`, `Input`, `All`, not code, so the server can export them.
  - **`Reduce(Set(...).FromInput(...))`** writes part of the input into the context. `Reset()`
    empties it; `Keep()` leaves it as it is.
- **`Differential`** lists sample inputs the definition is checked with, and **`Corpus`** hands
  the machine the answers to them, both below.

`SetLines` replaces the whole cart. There is no rule for "add one line": the browser keeps the cart
and sends it entire, which keeps the machine to rules the server can export and check, with no
code of its own.

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)

---

Next: [12.6 Serve it](/docs/book/checkout-is-a-state-machine/serve-it)
