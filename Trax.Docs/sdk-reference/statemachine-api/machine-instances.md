---
layout: default
title: Machine instances
description: Reference for IMachineInstances, which creates and advances system-owned machine instances from code, and how their ids are derived from a key.
parent: State Machine API
grand_parent: SDK Reference
nav_order: 13
---

# Machine instances


Most drafts belong to a user. Some work needs instances no user owns: one per partition of a source, say, each
moving through its stages on its own. `IMachineInstances.Start` creates those, from a train or at startup.
[`AddStateMachines`](/docs/sdk-reference/statemachine-api/add-trax-state-machines) registers it, scoped.

```csharp
public interface IMachineInstances
{
    Task<MachineInstance> Start<TMachine>(
        MachineKey key,
        JsonObject? context = null,
        CancellationToken cancellationToken = default
    ) where TMachine : IMachine;

    Task<AdvanceOutcome> Advance<TMachine>(
        MachineKey key,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    ) where TMachine : IMachine;
}

public sealed record MachineInstance(Guid Id, string Machine, string State, bool Created);
```

```csharp
var instance = await instances.Start<IngestMachine>(MachineKey.Of("orders-db", "partition-7"));
// instance.Created is true the first time, false on every call after it
```

## What Start does

It creates the instance in the machine's initial state with `context` (or the context the machine declares in
`StartsAt` when `context` is null), or returns the instance that already exists for the key. The context is
validated against the initial state as any stored snapshot is, and an invalid one, or one that makes the snapshot
larger than 64 KiB, throws `ArgumentException`. A machine that is not registered, or that does not declare
`SystemOwned()` in its `Configure`, throws `InvalidOperationException`.

When the initial state [invokes a train](/docs/statemachine/invoking-trains), the run is queued in the
transaction that inserts the row, and the row's invoke token names it; a `Start` that loses the race to create the
instance queues nothing.

A machine that declares `SystemOwned()` belongs to the system only: no user's draft operation reaches it, and the
four `stateMachine` mutations answer `unknown-machine` for it.

Calling `Start` twice with one key, from one host or from several at once, creates one row: the losing insert is a
lost race, and the call returns the winner's instance with `Created` false. The context of a call that finds an
existing instance is ignored.

The instance is owned by the system, not a user. No GraphQL operation creates, reads or advances it, and a user's
`loadSnapshot`, `saveSnapshot`, `advanceSnapshot` or `sendSnapshot` with its id does not reach it, even when that
user holds a draft under the same id. See
[persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports) for how the rows are kept apart. A
system instance is never expired by the draft TTL.

## What Advance does

It fires one trigger on the system instance for `key`, as the system: the code path through which a system
instance moves other than by its trains' outcomes, such as a `Retry` out of a failure state back into the state
that invokes the train.

```csharp
var outcome = await instances.Advance<IngestMachine>(MachineKey.Of("orders-db", "partition-7"), "Retry");
// AdvanceOutcome.Advanced: the instance is in its invoking state again, with a new run under a new token
```

It is checked exactly as a user's advance is: the edge's guard, the target state's context rule and the 64 KiB
cap. An invoked train's outcome trigger (`Ingesting.done`) is refused as `outcome-bound` and a trigger bound to the
machine's effect as `effect-bound`, so the system cannot forge what a run or an effect produced. Leaving an
invoking state cancels its run and entering one queues a new run, in the transaction that writes the snapshot,
and that run is authorized in the trusted scope like every run of a system-owned machine.

It returns an [`AdvanceOutcome`](/docs/sdk-reference/statemachine-api/persistence-ports): `Advanced` with the new
snapshot, `NotFound` when no instance exists for the key, `Rejected` with the engine's reason (`no-transition`,
`guard-failed`, `invalid-context`, ...), `LoadError` when the stored snapshot cannot be read, or `Conflict` when
the instance was written while the call ran (an outcome landed, say). Nothing is written unless it is `Advanced`.
A machine that is not registered, or not system-owned, throws `InvalidOperationException`.

No GraphQL operation calls it. A host that lets a person retry a system instance writes its own operation over it,
under its own authorization; the [Recovery sample](/docs/samples/recovery) does, for operators only.

## How the id is derived

```csharp
MachineKey.Of(params string[] parts)            // one or more parts; a string converts to a one-part key
MachineInstanceId.For(string machine, MachineKey key)
MachineInstanceId.Namespace(string machine)
MachineInstanceId.Root                          // 0d3cd492-3485-4ca5-8fdc-e20b35ed5a85
```

The id is an RFC 9562 version 5 UUID, in two steps:

1. The machine's namespace is the UUIDv5 of `MachineInstanceId.Root` and the UTF-8 bytes of the machine's name.
2. The instance id is the UUIDv5 of that namespace and the key's canonical encoding: for each part in order, the
   decimal count of its UTF-8 bytes, a colon, then the bytes.

The encoding can be split back into its parts, so two different keys never share an id: `("a|b", "c")`,
`("a", "b|c")` and `("a|b|c")` are three ids. A part may be empty; a null part or one holding an unpaired
surrogate is refused. The id changes if the machine is renamed, as every draft of the machine does. The root and
the encoding are fixed: changing either would orphan every instance already stored.
