---
authors: [Theauxm]
areas: [migrations, data-model]
status: accepted
---

# Every Trax timestamp is timestamptz, and the migrator's session is UTC

Every timestamp column in the `trax` schema is `timestamptz`, defaulting to `now()`. EF sends a UTC
`DateTime` as `timestamptz`, and Postgres converts that into a plain `timestamp` with the session's
`TimeZone`, so five columns that were `timestamp without time zone` stored a different wall-clock time
for a host whose session was not UTC, and variance schedules fired hours early or late. Migration 049
converts them, reading every stored value as UTC. The migrator pins its own sessions to UTC, so a script
that reads or writes a wall-clock time means UTC by it whatever the server's default.

## Status

**Accepted.**

## Considered options

**Pinning every application connection to UTC instead.** Rejected. It hides the mismatch rather than
removing it: any connection the consumer opens themselves (a report, a manual fix, another tool) still
writes the shifted value, and the column still cannot say what zone it meant.

**Sending `timestamp` from EF (legacy timestamp behaviour).** Rejected. It is a process-wide Npgsql switch
that changes every `DateTime` a consumer's own context sends, to fix five Trax columns.

## Consequences

049 changes the types without rewriting `work_queue`, `manifest_group` or `manifest`. Each block sets
its transaction's time zone to UTC and changes the type with no `USING`: in a UTC session Postgres
reads each stored value as UTC and keeps the table's storage, so the `ACCESS EXCLUSIVE` lock is held
for a catalog change, not a copy of the table. A `USING` clause, or a session in another zone, rewrites
the table under that lock, which on a large queue blocked enqueue and dispatch for the length of the
copy. A row written before 049 from a session outside UTC was already shifted when it was
stored, and nothing can tell which rows those were, so it keeps its shift.

## Exemplars

- `SessionTimeZoneTests` writes the five columns from New York and Berlin sessions and reads the same
  instants back, checks a defaulted `created_at` is the current instant, and refuses any
  `timestamp without time zone` column in the `trax` schema.
- `PostgresMigrationTests.cs` runs 049 from a New York session and checks each table keeps its filenode
  and a stored wall-clock time reads as the same UTC instant.
- [Database Migrations](/docs/migration-guides/database-migrations) says what 049 costs to apply.

Not covered: nothing checks the migrator's UTC pin on its own. A script that depends on it and the pin
going away would be caught only if a test runs that script from a non-UTC server default.

## Changelog

- **2026-09-30**: 049 edited before any real database ran it: UTC per block and no `USING`, so it no
  longer rewrites the tables.
- **2026-09-29**: Recorded.
