---
layout: default
title: Debugging with the Log Table
description: "Using the trax.log table written by AddDataContextLogging to debug across processes: what a row holds, how to query and correlate it, and what it drops."
parent: Effect
nav_order: 7
---

# Debugging with the Log Table

With `AddDataContextLogging()` enabled, `DataContextLoggingProvider` is registered as an
`ILoggerProvider` and log calls are persisted to the `trax.log` table. Three things filter
what lands there: the configured minimum level, the category blacklist, and one category
dropped unconditionally ahead of both, `Microsoft.EntityFrameworkCore.Database.Command`. That
last one is worth knowing before you go looking for SQL in this table, because no
configuration brings it back. An API host and a scheduler pointed at the same database
write into the same table, so one `psql` session reads both instead of two console streams
being correlated by hand.

Each row written while a train runs names that run in `metadata_id`, so one run's lines can be
read on their own. Read [What a row holds](#what-a-row-holds) before planning a query around
it: there is no timestamp and no train name, and a line written outside any run names none.

## What a row holds

| Column | Type | Holds |
|---|---|---|
| `id` | `bigint` | Identity, and the only ordering the table has |
| `metadata_id` | `bigint` | The `trax.metadata.id` of the run that wrote the line, or `0` for a line written outside any run. `NOT NULL` with a default of `0` from migration `065` |
| `event_id` | `integer` | The `EventId.Id` passed to the logging call |
| `level` | `trax.log_level` | `trace`, `debug`, `information`, `warning`, `error`, `critical`, `none` |
| `message` | `varchar` | The formatted message, truncated to 4000 characters |
| `category` | `varchar` | The logger category, truncated to 500 characters |
| `exception` | `varchar` | `Exception.Message`, truncated to 2000 characters |
| `stack_trace` | `varchar` | `Exception.StackTrace`, truncated to 4000 characters |

Three things about that shape catch people out.

**The level labels are lowercase.** `LogLevel` is mapped to the `trax.log_level` Postgres enum
declared in migration `002_log.sql`, and the labels there are `information`, `error` and the
rest. `level = 'Error'` matches nothing. The enum is ordered by severity, so `level >= 'error'`
is the range comparison you want.

**There is no timestamp column.** `id` is the insert order and the only time axis the table
has, which is why every query below orders by it. The primary key on `id` was dropped by
migration `004_log_pkey.sql` and restored by `021_log_performance.sql`, which also added the
`ix_log_metadata_id` index; `018_bigint_ids.sql` widened `id` and `metadata_id` from `integer`.

**`metadata_id` is the run on the logging call's async flow.** `ServiceTrain.Run` records its
row on the run's own flow, and the logger reads it when the call is made, so a line logged by a
junction, by anything the junction awaits, or by a lifecycle hook names the run. A train run
inside a junction names its own run, and the outer run's lines name it again once the inner run
returns. A line logged before the run's row has an id, or on a flow that is not a run's
(startup, a hosted service's own loop, a background writer such as the log writer itself),
stores `0`. Rows written before this was fixed store `0` too: migration `065` turns a `NULL` into
`0` and changes no other value.

The scheduler's polling is not on that list. The manifest manager and the job dispatcher are
service trains that record a `trax.metadata` row for every poll, so the lines their junctions
log name that internal run. Metadata cleanup deletes those rows, as it does for the scheduler's
other internal trains, and because there is no foreign key the log lines keep the id of a run
that no longer exists. The same holds for any run
whose row is deleted: a `metadata_id` with no matching `trax.metadata` row is a deleted run, not
a corrupt one. Join with `left join`, not `join`, when you want those lines too.

## Querying it

```bash
docker exec -it trax_database psql -U trax -d trax
```

```sql
-- recent activity
select id, level, category, message
from trax.log order by id desc limit 20;

-- failures only
select id, category, message, exception
from trax.log where level >= 'error' order by id desc limit 20;

-- everything one component logged
select id, level, message from trax.log
where category = 'MyApp.Trains.Combat.ResolveCombatTrain'
order by id desc limit 20;

-- free text, which is what stands in for a train filter
select id, level, category, message from trax.log
where message ilike '%order-4417%' order by id desc limit 50;
```

`category` is the `ILogger` category, which is the **implementation** type's FullName. That is
the opposite convention from `metadata.name`, which stores the interface FullName (see
[Train Discovery](/docs/mediator/train-discovery)). Filtering the log table by the interface
name finds nothing, and filtering `trax.metadata` by the class name finds nothing.

## Correlating a row with a train

`metadata_id` joins a row to its run, so one execution's lines come back on their own:

```sql
-- every line one run wrote, in order
select l.id, l.level, l.category, l.message
from trax.log l
join trax.metadata m on m.id = l.metadata_id
where m.external_id = '<external-id>'
order by l.id;
```

`trax.metadata` carries the train name, the external id, the timing and the host. For what
happened around a run (a hosted service's lines, or rows written before `metadata_id` was
filled), read the log rows on either side of it by id:

```sql
-- one train's history, from the table that actually stores the train name
select id, external_id, name, train_state, start_time, end_time, host_name
from trax.metadata
where name = 'MyApp.Trains.Combat.IResolveCombatTrain'
order by id desc limit 20;

-- a single execution: the metadata row is the whole record of it
select external_id, train_state, failure_junction, failure_reason, failure_exception,
       stack_trace, start_time, end_time
from trax.metadata where external_id = '<external-id>';

-- the log rows written around that run, narrowed by id range
select id, level, category, message from trax.log
where id between 128400 and 128900
order by id;
```

`external_id` is the identifier that survives a process boundary: an API writes it into
`trax.work_queue` when it queues work and the scheduler carries it onto the `trax.metadata` row
it creates. `trax.log` reaches it through `metadata_id`.

## Related tables

| Table | Holds |
|---|---|
| `trax.metadata` | train execution state, inputs, outputs, failure fields, timing, host |
| `trax.work_queue` | queued work, by `external_id` and `train_name`, before a run exists |
| `trax.junction_run` | each step a run took, by `metadata_id` and `position`, when the host calls [`AddJunctionEvents()`](/docs/effect/junction-events): which junction failed, with its failure class and exception type, and each question's answer |
| `trax.decision` | each decision a run made, by `metadata_id`, when the host calls [`AddDecisionRecording()`](/docs/effect/decisions#recording-decisions) |

## What the log table does not survive

`DataContextLoggingProvider` buffers into a bounded channel of 4096 and writes batches of up
to 256 as entries arrive. When the host stops it writes what is already queued, waiting up to five
seconds; only entries still queued after that are lost. A few consequences worth knowing before you
treat an absent row as evidence:

- The channel is `DropOldest`, so a burst larger than the buffer silently discards the oldest
  entries.
- Every entry is made storable before it is queued: a NUL character is removed from the message,
  category, exception and stack trace, and a field cut to its column length is cut on a character
  boundary, never through the middle of an emoji. The message keeps 4000 UTF-16 units, the
  category 500, the exception message 2000 and the stack trace 4000.
- When a batch still fails, its entries are written one at a time, so an entry the database
  refuses costs its own line and not the rest of the batch. An entry that fails on its own is
  dropped rather than retried, on the grounds that a logging failure should not take the host
  down.

Console and structured logging providers still run alongside this one. The database table is
the convenient shared view, not the system of record.

## SDK Reference

> [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging) | [Metadata](/docs/effect/metadata)
