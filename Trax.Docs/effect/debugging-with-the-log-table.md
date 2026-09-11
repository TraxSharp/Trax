---
layout: default
title: Debugging with the Log Table
parent: Effect
nav_order: 7
---

# Debugging with the Log Table

With `AddDataContextLogging()` enabled, every train log is persisted to the `trax.log` table.
That is what makes a failure traceable across process boundaries, where console output from
an API and a scheduler running separately would otherwise have to be correlated by hand.

## Querying it

```bash
docker exec -it trax_database psql -U trax -d trax
```

```sql
-- recent activity
select timestamp, level, message, train_name, train_external_id
from trax.log order by id desc limit 20;

-- one train's history
select timestamp, level, message from trax.log
where train_name = 'MyApp.Trains.Combat.IResolveCombatTrain'
order by id desc limit 20;

-- a single execution, across every process that touched it
select timestamp, message from trax.log
where train_external_id = '<external-id>' order by id;

-- failures only
select timestamp, message from trax.log where level = 'Error' order by id desc limit 20;
```

`train_name` is the train's canonical identifier, the interface FullName, which is the same
string stored in `metadata.name` and `work_queue.train_name`. Filtering by the concrete class
name finds nothing.

`train_external_id` is the useful one when a job crosses processes: an API queues work and a
scheduler runs it, and the external id is what ties the two halves together.

## Related tables

| Table | Holds |
|---|---|
| `trax.metadata` | train execution state, inputs, outputs, timing |
| `trax.junction_metadata` | junction-level detail within a run |

## SDK Reference

> [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging) | [Metadata](/docs/effect/metadata)
