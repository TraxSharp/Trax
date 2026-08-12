---
layout: default
title: Effects
parent: State Machine API
grand_parent: SDK Reference
nav_order: 7
---

# Effects

A machine's one consequential transition (send a letter, charge a card, provision a resource) binds an
`ISnapshotEffect`. Trax runs it exactly once when that transition is sent, records its receipt in the
snapshot, and never runs it twice, even across a crash and retry.

```csharp
public interface ISnapshotEffect
{
    Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default);
}
```

`Run` performs the side effect and returns a receipt, the downstream id (a message-log id, a charge id) that
proves it happened. Throwing means the effect did not complete: the transition is not applied, and the client
can retry from the same state.

## Binding it

Bind the effect inline on the transition with [`RunsOnce<TEffect>`](/docs/sdk-reference/statemachine-api/fluent-authoring),
and implement it in the host:

```csharp
m.In(CheckoutState.Review)
    .On(CheckoutTrigger.Pay)
        .When(Field((ReviewContext c) => c.Items).CountAtLeast(1))
        .RunsOnce<ICharge>()                       // keyPrefix defaults to "checkout:Pay"
        .Reduce(Set((PaidContext p) => p.Receipt).FromInput((PayInput i) => i.Receipt))
        .To(CheckoutState.Paid);
```

```csharp
public interface ICharge : ISnapshotEffect;

public sealed class StripeCharge(IPaymentGateway gateway) : ICharge
{
    public async Task<string> Run(Snapshot snapshot, CancellationToken ct)
    {
        var chargeId = await gateway.Charge(snapshot.Context, ct);
        return chargeId;                            // becomes the receipt
    }
}
```

Register the implementation like any service; the machine resolves `TEffect` from DI, so nothing is wired in
the composition root by hand:

```csharp
services.AddScoped<ICharge, StripeCharge>();
```

## Exactly-once and the receipt

The effect runs through the persistence layer's idempotent path: a claim is taken before the effect, held
under a lease with a fence token, and a crash mid-flight replays without re-running a completed effect. The
key is `{keyPrefix}:{userKey}:{id}`, so it is scoped per draft per user.

The receipt `Run` returns is handed to the transition's reducer as `input["receipt"]`, which is how the send
gets recorded in the destination context. A guard on the same edge can require it (so the transition only
completes once the effect has produced a receipt).
