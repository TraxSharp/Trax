---
layout: default
title: Effects
description: Reference for ISnapshotEffect, the side effect a state machine transition runs exactly once, its receipt, and cancelling or starting over.
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
proves it happened. Throwing means the effect did not complete: the transition is not applied, the draft is left as
it was, and the client can retry from the same state.

While the effect runs it is pending on the draft's row: the send marks the row before it runs the effect, every other
write to the draft (another event, a reset, a timer, a discard, the draft sweep) is refused as `effect-in-progress`
or waits, and the receipt, the move to the effect's target and the end of the mark are one write. An effect that ran
is therefore either still pending on its draft or recorded in it.

`Run(snapshot, effectKey, ct)` hands the effect its key: minted when the send marks the draft, the same for every run
of that send, and new for the next send of the event. Pass it to the system the effect calls as its idempotency key.

An `OperationCanceledException` is different: a charge can land and its response time out, so a cancellation
does not say whether the effect happened. The effect stays pending until its lease passes, and a send before then
is refused as `effect-in-progress`. Once the lease has passed on the database's clock, the next send of the event, or
the outcome reconciler on a host that registers the effect, takes it over and runs it again under the same key, and
records the result as the answer to the request that sent it. Each effect found past its lease is counted on
`StateMachineInstruments.EffectsAbandoned` (`trax.machine.effect.abandoned`). An effect neither runs from nor lands
in a state that invokes a train; such a machine refuses to build. The send reports it as `delivery-failed`
unless the request itself was cancelled. The send passes `Run` `CancellationToken.None`, not the request's token:
a client that disconnects mid-charge does not cancel the charge. Bound a slow downstream call with its own
timeout, and never after the irreversible step.

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
        var chargeId = await gateway.Charge(snapshot.Context);
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
key is `{keyPrefix}:{userKey}:{id}`, so it is scoped per draft per user. A system-owned instance's key is
`{keyPrefix}::{id}`, which no user's can equal, because a user key is never empty. A draft deleted by the draft TTL releases
the key, and so does a reset to the initial state once the effect's outcome is settled on the draft: a reset while
the effect runs, or after a receipt that never reached the draft, keeps the claim, and the next send replays its
receipt. See [what each path may write](/docs/sdk-reference/statemachine-api/persistence-ports#what-each-path-may-write).

Once the effect has returned, its receipt is recorded and the draft advanced on a token the request cannot
cancel. A client that disconnects right after a charge still leaves the draft showing the charge, and the next
send replays it instead of charging again. The receipt is recorded on the draft exactly as the effect loaded it,
because nothing else wrote the draft while the effect ran.

A host whose snapshot store is its own cannot hold the mark, and keeps a claim in the `effect_claim` ledger beside
the draft instead. There the receipt is recorded only on the draft as the effect loaded it: a draft saved, reset or
advanced while the effect ran makes the send report `conflict`, and the claim keeps the receipt with a fingerprint of
the content the effect ran on (`SnapshotFingerprint.Of(service.Serialize(snapshot))`). A later send replays it only
onto a draft whose content has that fingerprint, and is refused as `draft-changed` otherwise.

The transition itself is fired only by the send. An advance of its trigger is refused as `effect-bound`, and the
generic save, which serves only machines with no server-side behaviour, refuses every save of a machine with an
effect as `save-not-allowed`; see
[what each path may write](/docs/sdk-reference/statemachine-api/persistence-ports#what-each-path-may-write).

The receipt `Run` returns is handed to the transition's reducer as `input["receipt"]`, which is how the send
gets recorded in the destination context. It must be non-empty: a `null` or `""` receipt is treated as a failed
effect, the mark ends, and the send returns `delivery-failed` with the draft unchanged, exactly as if `Run` had
thrown. A guard on the same edge can require it (so the transition only
completes once the effect has produced a receipt).

## Cancelling and starting over

A draft is not deleted by the client. There is no delete mutation: a draft that is abandoned is left to the
[draft TTL](/docs/sdk-reference/statemachine-api/persistence-ports), and what a cancellation means is declared in
the machine, because only the domain knows what it undoes. What to do depends on where the draft is relative to
its effect.

**Before the effect runs**, nothing irreversible has happened. To start over, start a new draft: the old one
expires. A machine can also declare a reset edge back to its initial state, and the next send after it is a new
intent with a new key.

**While the effect runs**, the draft takes no other write: an event, a reset or a discard is refused as
`effect-in-progress` (`discard-not-allowed` for a discard) until the send settles. Show the user it is processing.

**After the effect ran**, the draft in its committed state is the record that it happened, and it stays as it is.
Undoing the effect is a second irreversible action (a refund, a void, releasing stock), not a delete. A machine runs
exactly one effect, so the compensation is a machine of its own whose one effect is the undo, started from the
receipt the first one recorded:

```csharp
public sealed class RefundMachine : Machine<RefundState, RefundTrigger>
{
    protected override void Configure(IMachineBuilder<RefundState, RefundTrigger> m)
    {
        m.Id("refund").Version(1).StartsAt(RefundState.Requested, () => new JsonObject());

        m.In(RefundState.Requested)
            .On(RefundTrigger.Refund)
            .RunsOnce<IRefund>()
            .Reduce((context, input) => new JsonObject
            {
                ["chargeId"] = context["chargeId"]?.DeepClone(),
                ["refundId"] = input?["receipt"]?.DeepClone(),
            })
            .To(RefundState.Refunded);

        m.In(RefundState.Refunded).Committed();
    }
}

public interface IRefund : ISnapshotEffect;

public sealed class StripeRefund(IPaymentGateway gateway) : IRefund
{
    public async Task<string> Run(Snapshot snapshot, CancellationToken ct)
    {
        var chargeId = snapshot.Context["chargeId"]?.GetValue<string>()
            ?? throw new InvalidOperationException("A refund names the charge it refunds.");
        return await gateway.Refund(chargeId, idempotencyKey: $"refund:{chargeId}");
    }
}
```

A refund draft whose context holds the checkout's receipt as `chargeId` is written by the host (the generic save
does not serve a machine with an effect), then the client sends `Refund`. A refund draft with no charge throws, so the effect has not run and the send can be retried. Trax runs the
refund once per refund draft. Two refund drafts for one charge are two drafts, so pass the downstream service an
idempotency key derived from the charge, as above, to make the refund once per charge. The checkout draft keeps
showing the charge, and the refund draft shows the refund; together they are the history a support request needs.
