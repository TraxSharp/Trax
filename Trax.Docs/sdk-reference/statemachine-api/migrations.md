---
layout: default
title: Migrations
parent: State Machine API
grand_parent: SDK Reference
nav_order: 6
---

# Migrations

A stored draft carries the definition `version` it was written against. When you change a machine's context
shape, bump `Version` and register a forward migration so an older stored draft is upgraded on rehydrate
instead of being rejected.

```csharp
IMachineBuilder<TState, TTrigger> MigrateFrom(
    int fromVersion,
    Func<string, JsonObject, MigrationResult> migrate);
```

| Parameter | Meaning |
| --- | --- |
| `fromVersion` | the stored version this step upgrades. Register one per source version to chain across several. |
| `migrate` | given the stored state name and context, returns the upgraded `MigrationResult(string State, JsonObject Context)`. Compute a fresh context; never mutate the input. |

Migration runs on rehydrate, before the target state's context rule is checked, so the upgraded context must
satisfy the new schema. A draft whose version has no migration path to the current definition rehydrates as
[`version-mismatch`](/docs/sdk-reference/statemachine-api/result-codes); the client starts fresh rather than
misreading it.

## Example

`checkout` v2 adds a denormalised `total`. A v1 draft has no `total`, so it would fail v2 validation; the
migration backfills it:

```csharp
m.Id("checkout").Version(2).StartsAt(CheckoutState.Cart, Fresh)
    .MigrateFrom(1, (state, ctx) =>
    {
        var next = (JsonObject)ctx.DeepClone();
        next["total"] = ItemsCount(ctx) * UnitPriceCents;   // backfill from the item count
        return new MigrationResult(state, next);
    });
```

## Pinning correctness

A migration is guarded by a **migration golden**, `machines/<machine>/migration.json`: a committed set of
stored older-version snapshots and the exact [canonical wire](/docs/statemachine#the-canonical-wire) each must
become. Both runtimes replay it, so a migration that drops, renames, or reorders a surviving field fails, and
the two runtimes cannot upgrade the same draft differently. Where the differential guards machine logic, the
migration golden guards schema evolution.
