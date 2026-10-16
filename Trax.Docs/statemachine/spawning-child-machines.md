---
title: Spawn a child machine from a state
description: "Make a state start a child state machine with Spawns, hear how it ends through OnDone, OnFailed and OnCancelled, and find the child from the parent."
type: how-to
area: machines
nav_order: 4
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Invokes.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/InvokeOutbox.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/SpawnsStartupValidator.cs
---

# Spawn a child machine from a state

A state can start another machine and wait for it to end. Entering the state creates the child; the child moves
through its own events, and when it reaches a state it ends in, the parent takes the matching outcome, as it would
from a train it [invoked](/docs/statemachine/invoking-trains). Leaving the state removes the child. Use it for a flow
with sub-flows: an order whose parcel is packed by its own machine, or a wizard one of whose steps is a wizard.

## 1. Mark where the child ends

On the child machine, declare each state it ends in. `Ends()` gives the parent `OnDone`, with the child's context;
`Fails()` gives it `OnFailed`. Nothing else about the child changes: it is an ordinary machine, with its own events,
rules and states.

```csharp fragment
public sealed class ParcelMachine : Machine<ParcelState, ParcelTrigger>
{
    protected override void Configure(IMachineBuilder<ParcelState, ParcelTrigger> m)
    {
        m.Id("parcel").Version(1).StartsAt(ParcelState.Packing, () => new JsonObject { ["label"] = "" });

        m.In(ParcelState.Packing)
            .Context<ParcelContext>()
            .On(ParcelTrigger.Seal)
            .WithInput<SealInput>()
            .Reduce(Set((ParcelContext c) => c.Label).FromInput((SealInput i) => i.Label))
            .To(ParcelState.Packed)
            .On(ParcelTrigger.Drop)
            .To(ParcelState.Damaged);

        m.In(ParcelState.Packed).Context<ParcelContext>().Ends();
        m.In(ParcelState.Damaged).Context<ParcelContext>().Fails();
    }
}
```

The child's initial state must wait for an event: it may not invoke a train or spawn a child of its own, because a
spawned child is created in that state with nothing started there.

## 2. Spawn it from the parent's state

`Spawns<TChild, TContext, TOutput>(childId, context)` names the child machine, the field of the state's context the
server writes the child's id into (a `Guid?`), and optionally how the child's initial context is built from the
parent's. It returns the same builder an invoking state does, so the outcomes are declared the same way:

```csharp fragment
m.In(OrderState.Packing)
    .Context<OrderContext>()
    .Spawns<ParcelMachine, OrderContext, ParcelResult>(c => c.ParcelId)
    .OnDone(OrderState.Sent,
        reduce: Set((OrderContext c) => c.Label).FromInput((ParcelResult r) => r.Label))
    .OnFailed(OrderState.Lost)
    .OnCancelled(OrderState.Recalled)
    .On(OrderTrigger.Abandon)
    .To(OrderState.Abandoned);
```

- `OnDone` reads the child's context at its end as the outcome's input, but only the fields `TOutput` names
  (`ParcelResult` has `Label`); the rest of the child's context is not passed on. Guard and reduce it as you would a
  train's output.
- `OnFailed` is taken when the child enters a `Fails()` state.
- `OnCancelled` is taken when the child disappears before it ends: host code or an operator deleted it.
- Leaving the state through one of its own transitions (`Abandon` here) removes the child, and whatever the child
  itself was waiting on.

The rules [an invoking state follows](/docs/statemachine/invoking-trains#2-declare-the-invoking-state) apply here too:
an ordinary event may not enter an outcome's target, and an event any caller may send may not copy into a field an
outcome writes, the child's id field included.

## 3. Find the child from the parent

The child belongs to whoever owns the parent. On a user-owned machine it is the same user's instance: the client
reads the parent, takes the id from `parcelId`, and drives the child through the child machine's own operations, under
the child machine's rules. On a system-owned machine the child must be system-owned too, and host code drives it with
[`IMachineInstances.Advance<TMachine>(id, ...)`](/docs/sdk-reference/statemachine-api/machine-instances).

While the parent waits, the child cannot be discarded by its owner, saved by a client or swept as an idle draft. Once
the parent has taken its outcome, the child is an ordinary instance in its end state.

## 4. Meet the host's requirements

The host checks, at startup, that every spawning state can start its child and hear from it:

- the child machine is registered with `AddStateMachines`;
- it is owned the way its parent is, and declares at least one `Ends()` or `Fails()` state;
- its initial state invokes nothing and spawns nothing;
- the data provider is Postgres or SQLite, so the child and the parent's move are written together;
- on a user-owned parent, no outcome or timer enters the spawning state, since no user would be present.

The write that ends a child tells its parent at once. The reconciler's sweep, which every host that registers the
machines runs, delivers whatever that missed.

## SDK Reference

> [IStateBuilder.Spawns](/docs/sdk-reference/statemachine-api/machine#istatebuilder) | [IInvokeBuilder](/docs/sdk-reference/statemachine-api/machine#iinvokebuilder) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances)
