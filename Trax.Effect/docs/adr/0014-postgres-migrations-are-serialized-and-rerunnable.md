---
authors: [Theauxm]
areas: [migrations, providers]
status: accepted
---

# Postgres migrations are serialized across hosts and every script can run again

The Postgres migrator runs each script without a transaction, one statement at a time, and holds a
session advisory lock for the whole run, so hosts starting together against one database migrate one
after another. Because a script that stops partway keeps what it did and is not journaled, it runs again
from its first statement at the next start, so every script from 046 on is written to be run again: each
statement is guarded (`IF NOT EXISTS`, `IF EXISTS`, or a `DO` block that checks first). An index on
`metadata`, `log`, `work_queue` or `snapshot_draft` is built `CREATE INDEX CONCURRENTLY`, and the migrator drops an index
a script builds that an interrupted build left `INVALID` before it runs the scripts, so the script builds
it again. An invalid index no script builds is not the migrator's, and it is left alone. The scripts run
with a five-second `lock_timeout`, and when one gives up on its lock (`55P03`) the migrator runs the
pending scripts again, up to ten times, before it fails the start.

## Status

**Accepted.**

## Considered options

**A transaction per script, as the Sqlite migrator has.** Rejected. A script would land whole or not at
all, but `CREATE INDEX CONCURRENTLY` cannot run in a transaction, so every index on the hot tables would
go back to a plain build that blocks their writers for as long as it takes (0.38 s of blocked inserts at
two million `metadata` rows, measured). And a type change that rewrites a large table would hold its
lock and the whole script's work in one transaction.

**No lock, relying on idempotence alone.** Rejected. Two hosts migrating a fresh database ran the same
DDL side by side, and one crashed on an object the other had just created (`42704`, `42710`), three
times out of three. `IF NOT EXISTS` is not atomic against a concurrent creator.

**Blocking in `pg_advisory_lock`.** Rejected for polling with `pg_try_advisory_lock`. A session blocked
in `pg_advisory_lock` is inside a statement and holds a snapshot while it waits, and
`CREATE INDEX CONCURRENTLY` waits for every older snapshot: the host holding the lock waited on the host
waiting for it, and Postgres does not see that as a deadlock.

**No lock timeout on the script session.** Rejected on 2026-09-30. `UsePostgres` migrates at startup,
so in a rolling deploy the first new instance's `ALTER TABLE` queued behind any transaction an old
instance held open, and every enqueue and dispatch on every instance queued behind the `ALTER`. A
script that gives up after five seconds and runs again lets that traffic through between tries.

**Dropping every invalid index in `trax`.** Rejected on 2026-09-30. It dropped a DBA's in-flight
`REINDEX CONCURRENTLY` (its `_ccnew` copy) and a consumer's own failed build, which nothing recreates.
The migrator now drops only names a script creates, read from the scripts themselves.

## Consequences

The rule starts at 046. Scripts before it shipped as they are, and several of them (007, 010, 011, 014,
021, 022, 027) cannot run twice: a database left partway through one of those still needs a hand fix.

A host whose migration hangs holds every other host at startup, because the wait for the advisory lock
has no timeout. That is the same place a host that cannot reach its database fails (`effect/0001`).

`CREATE INDEX CONCURRENTLY` waits for transactions already open on the table, and that wait is a lock
wait, so the five-second `lock_timeout` applies to it too. A script adding an index behind a transaction
that stays open for the whole retry budget (about a minute) fails the start with `55P03`, and the host
starts once that transaction ends. The retried build leaves an invalid index, which the migrator drops
before the next try.

The migrator also pins its sessions' time zone to UTC (`effect/0015`).

## Exemplars

- `PostgresMigrationRerunTests` reads every script from 046 on and refuses a statement with no guard, and
  a plain index build on `metadata`, `log`, `work_queue` or `snapshot_draft`.
- `PostgresMigrationTests.cs` runs four migrations of an empty database at once, runs every script from
  046 on again over a migrated database, rebuilds an index left invalid by a failed concurrent build,
  leaves alone an invalid index no script builds, and, behind a held table lock, gives up after its
  tries or runs the scripts again once the lock is released.
- [Writing Migrations](/docs/reference/writing-migrations) is the rule this produces.

Not covered: the guard reads the SQL as text, so it sees that a `DO` block checks something, not that it
checks the right thing, and it accepts any `UPDATE` or `DELETE`, whose `WHERE` decides whether a second
run changes anything.

## Changelog

- **2026-10-08**: `snapshot_draft` joins the tables whose indexes are built concurrently: its drafts are
  written all the time and it may hold millions of rows.
- **2026-09-30**: The script session has a five-second `lock_timeout` with bounded retries, and the
  migrator drops only the invalid indexes a script builds.
- **2026-09-29**: Recorded.
