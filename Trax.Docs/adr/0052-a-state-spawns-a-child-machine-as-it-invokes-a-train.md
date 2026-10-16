---
authors: [Theauxm]
repos: [effect, api]
areas: [platform, data-model]
status: accepted
---

# A state spawns a child machine as it invokes a train, and the child belongs to its parent's owner

A flow with sub-flows (an order whose lines each run their own fulfilment, a wizard whose step is itself a wizard) was
one large machine or several unrelated ones joined by hand. A state now declares
`Spawns<TChild, TContext, TOutput>(c => c.ChildId)`: entering it creates the child, the child's end comes back as the
state's outcome, and leaving the state removes the child. It is [0046](./0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md)
with a machine where the train was, so it reuses that decision's token, outcome triggers, delivery and sweep rather
than adding a second way for one machine to wait on another.

## Status

**Accepted.**

## Why this is written down

Because two other shapes were on the table, and each would have cost something this one does not.

**Nested states inside one machine** (statechart regions) keep one snapshot and need no delivery between rows, but the
snapshot, the IR and the TypeScript engine would all grow nested regions, and a child could not outlive the view of
its parent. **Leaving it to trains** (a train starts children, a system machine polls them) costs nothing, and keeps
the gap: the parent cannot be shown, cancelled or reasoned about as one thing.

### Shape

**A child is an invoke target.** The spawning state is an invoking state to everything that already handles one: its
`OnDone`, `OnFailed` and `OnCancelled` are declared and checked the same way, its outcome triggers are
`<State>.done`, `.failed` and `.cancelled`, and they export to the IR unchanged, so the TypeScript twin needs nothing
new and no IR hash moves. A client cannot send an outcome trigger, so it cannot forge a child's end.

**A child says where it ends.** A state declared `Ends()` gives the parent `OnDone`, with the fields of the child's
context the parent's `TOutput` names (and no others); one declared `Fails()` gives `OnFailed`. A child that disappears
before it ends (removed by host code, or by an operator) gives `OnCancelled`.

**The parent learns the child's id from its own context.** The server writes the id into a `Guid?` field of the
spawning state's context when it starts the child, so the client that moved the parent reads where the child is. The
field is a server write: an event any caller may send cannot copy into it.

### Ownership

**A child belongs to its parent's owner.** A user-owned parent's child is that user's instance, driven by the user's
own events and reads under the child machine's rules; a system parent's child is the system's, driven through
`IMachineInstances.Advance<TMachine>(id, ...)`. The startup check refuses a pair owned two ways. The alternative, a
system-owned child under a user's parent, would have needed a way for the user to act on an instance they do not own.

**Only an event the user sends starts a child of a user-owned parent**, as only it may queue a run under 0046: an
outcome or a timer that enters a spawning state on such a machine is refused at startup.

### Correlation and delivery

**The child carries the parent's token.** Entering the spawning state sets the parent's `invoke_token` and inserts the
child with the same value in `spawned_by`, in one transaction. A child's end is applied with the parent's conditional
`UPDATE ... WHERE invoke_token = @token`, so it lands once and only on the entry that started it.

**The write that ends a child delivers it; the sweep guarantees it.** An advance, a system advance, an outcome or a
timer that puts a spawned child in an end state delivers to the parent after it commits. The reconciler's sweep also
pages through children in an end state whose parent still holds the token, and through parents whose child is gone.

**Leaving the state removes the child,** in the same transaction, after cancelling the run or removing the child the
child itself waits on. While the parent waits, the child cannot be discarded, saved by a client or swept as an idle
draft; once the parent has moved on it is an ordinary instance.

## Exemplars

**Enforced elsewhere:** `SpawnedChildTests` in `Trax.Effect.StateMachine.Persistence.Integration` drives the flow on
Postgres and SQLite (the child is created for the parent's owner, its end and its failure move the parent, leaving
removes it and what it waits on, the sweep delivers an end its write did not and a child that disappeared, and a
waiting parent's child cannot be discarded, saved or answered for by a forged outcome); `SpawnsStartupValidatorTests`
in the same project pins the startup refusals; `SpawnDeclarationTests` in `Trax.Effect.StateMachine.Tests` pins the
builder and the IR export.

- [Spawn a child machine from a state](/docs/statemachine/spawning-child-machines) is the rule as published.

Not covered: a child's initial state may not invoke a train or spawn a child of its own, since a spawned child is
inserted with nothing started; and there is no fan-out (one state, several children) yet.

## Changelog

- **2026-10-16**: Recorded.
