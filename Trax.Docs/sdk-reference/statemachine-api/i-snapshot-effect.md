---
title: ISnapshotEffect
description: Reference for ISnapshotEffect, the side effect a state machine transition runs exactly once, and the receipt it records on the draft.
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 7
package: Trax.Effect.StateMachine.Persistence
namespace: Trax.Effect.StateMachine.Persistence
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/Ports.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/SnapshotEffectRunner.cs
redirect_from:
  - /docs/sdk-reference/statemachine-api/effects
---

# ISnapshotEffect

The irreversible side effect of a machine's one consequential transition (send a letter, charge a card, provision a
resource). A transition binds it with `RunsOnce<TEffect>`; Trax runs it once when that transition is sent, records
its receipt in the snapshot, and does not run it again, a crash and retry included.

## Signature

```csharp fragment
namespace Trax.Effect.StateMachine.Persistence;

public interface ISnapshotEffect
{
    Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default);

    Task<string> Run(Snapshot snapshot, string effectKey, CancellationToken cancellationToken) =>
        Run(snapshot, cancellationToken);
}
```

## Members

| Member | Description |
|--------|-------------|
| `Run(Snapshot snapshot, CancellationToken cancellationToken)` | Performs the side effect for the draft as loaded, in the effect's from-state, and returns its receipt: the downstream id (a message-log id, a charge id) that proves it happened. |
| `Run(Snapshot snapshot, string effectKey, CancellationToken cancellationToken)` | The same, given the effect's key. The effect runner calls this overload, with `CancellationToken.None`. The default ignores the key and calls the first overload. |

## Returns

A non-empty receipt. It is handed to the transition's reducer as `input["receipt"]`, which is how the send is
recorded in the destination context. A `null` or `""` receipt is treated as a failed effect: the mark ends, and the
send returns `delivery-failed` with the draft unchanged, as if `Run` had thrown. A guard on the same edge can require
the receipt.

## Behavior

`Run` performs the side effect and returns a receipt, the downstream id (a message-log id, a charge id) that
proves it happened. Throwing means the effect did not complete: the transition is not applied, the draft is left as
it was, and the client can retry from the same state.

While the effect runs it is pending on the draft's row: the send marks the row before it runs the effect, every other
write to the draft (another event, a reset, a timer, a discard, the draft sweep) is refused as `effect-in-progress`
or waits, and the receipt, the move to the effect's target and the end of the mark are one write. An effect that ran
is therefore either still pending on its draft or recorded in it.

`Run(snapshot, effectKey, cancellationToken)` hands the effect its key: minted when the send marks the draft, the same for every run
of that send, and new for the next send of the event. Pass it to the system the effect calls as its idempotency key.

An `OperationCanceledException` is different: a charge can land and its response time out, so a cancellation
does not say whether the effect happened. The effect stays pending until its lease passes, and a send before then
is refused as `effect-in-progress`. Once the lease has passed on the database's clock, the next send of the event, or
the outcome reconciler on a host that registers the effect, takes it over and runs it again under the same key, and
records the result as the answer to the request that sent it. Each effect found past its lease is counted on
`StateMachineInstruments.EffectsAbandoned` (`trax.machine.effect.abandoned`). The send reports it as `delivery-failed`
unless the request itself was cancelled. The send passes `Run` `CancellationToken.None`, not the request's token:
a client that disconnects mid-charge does not cancel the charge.

An effect neither runs from nor lands in a state that invokes a train; such a machine refuses to build.

The implementation is resolved from the container as `TEffect` when the transition is sent.

### Exactly-once and the receipt

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

On the server the transition is fired only by the send. The server refuses an advance of its trigger as
`effect-bound` (the draft service and `IMachineInstances.Advance` both check it); the bare engine does not:
`SnapshotMachine.Advance` fires the edge like any other, so code that drives the engine directly is not held to
the send. The generic save, which serves only machines with no server-side behaviour, refuses every save of a
machine with an effect as `save-not-allowed`; see
[what each path may write](/docs/sdk-reference/statemachine-api/persistence-ports#what-each-path-may-write).

The receipt `Run` returns is handed to the transition's reducer as `input["receipt"]`, which is how the send
gets recorded in the destination context. It must be non-empty: a `null` or `""` receipt is treated as a failed
effect, the mark ends, and the send returns `delivery-failed` with the draft unchanged, exactly as if `Run` had
thrown. A guard on the same edge can require it (so the transition only
completes once the effect has produced a receipt).

### After the effect ran

A draft whose effect ran stays in its committed state as the record that it happened. Undoing the effect is a
second irreversible action, run by a machine of its own: see
[Undo an effect that already ran](/docs/statemachine/undo-an-effect).

## Example

The transition binds the effect, from the State Machine sample:

```csharp from=Trax.Samples/samples/StateMachine/Trax.Samples.StateMachine/Machines.cs
.On(CheckoutTrigger.Pay)
.AnyAuthenticated()
.WithInput<PayInput>()
.When(All(HasItems, Input((PayInput i) => i.Receipt).NonEmpty()))
.Because("Checkout needs items and a receipt to be paid.")
.RunsOnce<ICharge>("checkout:charge")
.Reduce(Set((PaidContext c) => c.Receipt).FromInput((PayInput i) => i.Receipt))
.To(CheckoutState.Paid);
```

The host implements and registers it, passing the effect's key on as the downstream idempotency key:

```csharp compile
builder.Services.AddScoped<ICharge, GatewayCharge>();

public sealed class GatewayCharge(IPaymentGateway gateway) : ICharge
{
    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The effect runner passes the effect's key.");

    public Task<string> Run(Snapshot snapshot, string effectKey, CancellationToken cancellationToken) =>
        gateway.Charge(snapshot.Context, effectKey, cancellationToken);
}
```

## See also

- [Undo an effect that already ran](/docs/statemachine/undo-an-effect).
- [Machine](/docs/sdk-reference/statemachine-api/machine#itransitionbuilder), `RunsOnce<TEffect>` on the transition builder.
- [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports#what-each-path-may-write), what each path may write while an effect is pending.
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes).
