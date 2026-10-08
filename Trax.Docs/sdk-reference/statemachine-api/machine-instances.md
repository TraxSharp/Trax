---
layout: default
title: Machine instances
description: Reference for IMachineInstances.Start, which creates system-owned machine instances from code, and how their ids are derived from a key.
parent: State Machine API
grand_parent: SDK Reference
nav_order: 13
---

# Machine instances

**Experimental.** `IMachineInstances` and the types beside it ship under the diagnostic `TRAXEXP002`, which
reports as an error until you opt in:

```xml
<NoWarn>$(NoWarn);TRAXEXP002</NoWarn>
```

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
larger than 64 KiB, throws `ArgumentException`. A machine that is not registered throws
`InvalidOperationException`.

Calling `Start` twice with one key, from one host or from several at once, creates one row: the losing insert is a
lost race, and the call returns the winner's instance with `Created` false. The context of a call that finds an
existing instance is ignored.

The instance is owned by the system, not a user. No GraphQL operation creates, reads or advances it, and a user's
`loadSnapshot`, `saveSnapshot`, `advanceSnapshot` or `sendSnapshot` with its id does not reach it, even when that
user holds a draft under the same id. See
[persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports) for how the rows are kept apart. A
system instance is never expired by the draft TTL.

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
