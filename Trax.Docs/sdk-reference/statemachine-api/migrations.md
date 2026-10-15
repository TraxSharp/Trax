---
title: Migrations
description: "Reference for MigrateFrom, which registers a forward migration so an older stored state machine draft is upgraded on rehydrate instead of rejected."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 6
package: Trax.Effect.StateMachine
namespace: Trax.Effect.StateMachine
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Fluent.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/SnapshotMachine.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/MachineDefinition.cs
---

# Migrations

A stored draft carries the definition `version` it was written against. `MigrateFrom` registers a forward migration
from one older version, so a draft stored at that version is upgraded when it is rehydrated instead of being
refused.

## Signature

```csharp fragment
IMachineBuilder<TState, TTrigger> MigrateFrom(
    int fromVersion,
    Func<string, JsonObject, MigrationResult> migrate);

public sealed record MigrationResult(string State, JsonObject Context);
```

## Parameters

| Parameter | Meaning |
| --- | --- |
| `fromVersion` | The stored version this step upgrades. One step is registered per source version; steps chain across several versions. |
| `migrate` | Given the stored state name and context, returns the upgraded `MigrationResult(string State, JsonObject Context)`. It computes a fresh context and does not mutate its input. |

## Returns

The `IMachineBuilder<TState, TTrigger>`, for chaining.

## Behavior

Migration runs on rehydrate, before the target state's context rule is checked, so the upgraded context must satisfy
the current definition's schema. Every path that reads a snapshot rehydrates it:

| Path | What happens to an older snapshot |
| --- | --- |
| `loadSnapshot` | A stored older draft comes back at the current version. The row itself is rewritten only by the next save, advance or send. |
| `advanceSnapshot`, `sendSnapshot` | The stored older draft is upgraded first, then the trigger fires on the upgraded draft, and the result is stored at the current version. |
| `saveSnapshot` | A snapshot an older client sends at an older version is upgraded, validated, and stored at the current version; the response carries the upgraded snapshot. |

A draft whose version has no migration path to the current definition, or is newer than it, rehydrates as
[`VERSION_MISMATCH`](/docs/sdk-reference/statemachine-api/result-codes).

The machines in `Trax.Api.StateMachine/machines/` pin a migration with a migration golden,
`machines/<machine>/migration.json`: a committed set of stored older-version snapshots and the exact
[canonical wire](/docs/statemachine/canonical-wire) each must become. The tests of both runtimes replay it, so a migration that drops, renames or reorders a surviving field fails, and the two runtimes
cannot upgrade the same draft differently.

## Example

The State Machine sample's `checkout` version 2 adds a denormalised `total`. A version 1 draft has none, so it would
fail version 2 validation; the migration backfills it:

```csharp from=Trax.Samples/samples/StateMachine/Trax.Samples.StateMachine/Machines.cs
m.Id("checkout")
    .Version(2)
    .StartsAt(CheckoutState.Cart, Fresh)
    // v1 -> v2: backfill total from the item count so a pre-total draft stays valid under v2.
    .MigrateFrom(
        1,
        (state, ctx) =>
        {
            var next = (JsonObject)ctx.DeepClone();
            next["total"] = ItemsCount(ctx) * UnitPriceCents;
            return new MigrationResult(state, next);
        }
    )
```

## See also

- [Machine](/docs/sdk-reference/statemachine-api/machine#imachinebuilder), `Version` and `MigrateFrom` on the builder.
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes).
