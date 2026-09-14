---
layout: default
title: Debugging with the Log Table
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

What it does not give you is per-execution correlation. Read
[What a row holds](#what-a-row-holds) before planning a query around it: there is no
timestamp, no train name, and the column that would tie a row to a train run is never
written.

## What a row holds

| Column | Type | Holds |
|---|---|---|
| `id` | `bigint` | Identity, and the only ordering the table has |
| `metadata_id` | `bigint` | Intended reference to `trax.metadata.id`, never populated |
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

**`metadata_id` is always `0`.** `Log.Create` builds a row from level, message, category, event
id and exception, and nothing on the write path sets `MetadataId`, whose setter is private. The
index on the column, the dashboard's metadata detail page and the GraphQL `logs` query's
`metadataId` filter are all wired for a correlation nothing produces.

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

The only link the schema offers is `metadata_id`, and since it is never written, the join
returns nothing:

```sql
-- correct, and empty, until metadata_id is populated
select l.id, l.level, l.message
from trax.log l
join trax.metadata m on m.id = l.metadata_id
where m.external_id = '<external-id>'
order by l.id;
```

What works instead is to find the run in `trax.metadata`, which does carry the train name, the
external id, the timing and the host, and then read the log rows on either side of it:

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
it creates. It never reaches `trax.log`.

## Related tables

| Table | Holds |
|---|---|
| `trax.metadata` | train execution state, inputs, outputs, failure fields, timing, host |
| `trax.work_queue` | queued work, by `external_id` and `train_name`, before a run exists |

## What the log table does not survive

`DataContextLoggingProvider` buffers into a bounded channel of 4096 and flushes batches of up
to 256 once a second. Two consequences worth knowing before you treat an absent row as
evidence:

- The channel is `DropOldest`, so a burst larger than the buffer silently discards the oldest
  entries.
- A failed flush catches everything and drops the whole batch rather than retrying, on the
  grounds that a logging failure should not take the host down.

Console and structured logging providers still run alongside this one. The database table is
the convenient shared view, not the system of record.

## SDK Reference

> [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging) | [Metadata](/docs/effect/metadata)
