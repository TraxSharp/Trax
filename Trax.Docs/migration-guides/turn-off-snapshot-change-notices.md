---
title: Turn off state-machine change notices
description: "The order to move every Postgres host to polling and then switch off the trax.snapshot_notify trigger that migration 080 added, and how to turn it back on."
type: how-to
area: data
nav_order: 12
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations/080_snapshot_draft_change_notify_switch.sql
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/ChangeFeedStartupValidator.cs
---

# Turn off state-machine change notices

On Postgres every write to `trax.snapshot_draft` sends a change notice, and every transaction that
sends one takes a lock shared by the whole cluster through its commit, so concurrent instance writes
commit one at a time. Migration 080 lets the database stop sending them; hosts then hear changes by
polling. Why the switch exists and what each host checks at startup is in
[Schema migrations](/docs/reference/schema-migrations#state-machine-change-notify-switch-080).

The order matters. A host still listening never hears a write the database no longer announces,
and a host on `Notify` refuses to start once the database turns the notices off.

## 1. Move every host to polling

Set `ChangeFeed` to `Poll` in each host's `AddStateMachines` options, and deploy every host that
serves state machines from this database:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;

services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(options => options.ChangeFeed = StateMachineChangeFeed.Poll, assemblies)
        .AddMediator(assemblies)
);
```

A polling host whose database still sends notices starts and logs a warning saying so. That is
expected until step 2.

## 2. Turn the notices off for the database

Once no host is on `Notify`, run:

```sql
ALTER DATABASE <name> SET trax.snapshot_notify = 'off';
```

It applies to new sessions, so connections already open keep sending notices until the pool
replaces them. Use only this database-level setting: a role setting, a connection string's
`Options=-c trax.snapshot_notify=off` or `SET LOCAL` silences some writers only, and is not
supported while any host listens.

## 3. Check the warning is gone

Restart one host. It no longer logs that the database still sends a change notice for every write.

## Turning the notices back on

Reset the setting first, then move hosts back to `Notify`:

```sql
ALTER DATABASE <name> RESET trax.snapshot_notify;
```

Also `RESET` it for any role or connection string that sets it. A `Notify` host started while the
notices are off refuses to start, and one that was already running when they were turned off stays
silent until it restarts.

## Related

- [AddStateMachines options](/docs/sdk-reference/statemachine-api/add-state-machines#options): `ChangeFeed` and the other options.
- [Schema migrations](/docs/reference/schema-migrations#state-machine-change-notify-078): the trigger 078 added.
