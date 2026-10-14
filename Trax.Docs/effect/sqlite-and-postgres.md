---
title: SQLite and Postgres
description: "How Trax's SQLite and Postgres data providers differ: infrastructure, concurrent writes, coordination across processes, and where each one fits."
type: explanation
area: data
nav_order: 21
---

# SQLite and Postgres

Trax has two database providers, and both carry every feature: the scheduler's full manifest manager, the job
dispatcher, metadata cleanup and the rest of the database-backed services, because both set `HasDatabaseProvider`.
Only the in-memory provider runs the scheduler's reduced set. What separates the two is how far they coordinate.

| | SQLite | PostgreSQL |
|---|---|---|
| Infrastructure | None: a file | A running server |
| Persistence | Yes | Yes |
| Full scheduler | Yes | Yes |
| Concurrent writes | Serialized at the file (WAL mode lets reads continue) | Concurrent |
| `FOR UPDATE SKIP LOCKED` | No | Yes |
| Coordination across processes | No | Yes, through advisory locks |
| Queued-work notices | In-process only | Every host on the database (`LISTEN`/`NOTIFY`) |

SQLite serializes writes at the file, which keeps a single process from dispatching one job twice. It does nothing
for a second process: there is no lock that two processes on one file can both see in the way the Postgres provider
uses advisory locks and row locks to share the work queue, elect the scheduler's leader and serialize migrations.
So SQLite fits local development, integration tests and a deployment that is one process on one machine. Anything
that runs more than one Trax process against the same data, such as a hub with workers or two replicas behind a load
balancer, needs Postgres.

Moving between them is a change of one call (`UseSqlite` to `UsePostgres`), with one thing to watch: SQLite stores
enums as integers and JSON as text, so raw SQL written against one does not run against the other unchanged.

## SDK Reference

> [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory)
