---
layout: default
title: Database Migrations
parent: Reference
nav_order: 20
---

# Database Migrations

`UsePostgres` and `UseSqlite` apply every pending migration at startup, so most upgrades need
nothing from you. The migrations on this page change something you can see: a column your code
reads, an index worth knowing about on a large database, or how an existing row behaves after
the upgrade. If your host calls [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations),
run them yourself before the new version serves traffic.

Postgres and SQLite number their migrations independently, so where both have one, both names
are given.

## ManifestGroup (014)

`014_manifest_group.sql` promotes a manifest's group from a denormalized string (`group_id`) to
its own table with per-group dispatch controls. It:

1. creates `trax.manifest_group` with `name`, `max_active_jobs`, `priority`, `is_enabled` and
   timestamp columns;
2. seeds a `manifest_group` row for each distinct `group_id` on existing manifests;
3. adds a NOT NULL `manifest_group_id` foreign key to `trax.manifest`;
4. drops the old `group_id` column.

It is idempotent, and existing manifests keep their groups.

`Manifest.GroupId` (`string?`) is replaced by `Manifest.ManifestGroupId` (`int`) and the
`Manifest.ManifestGroup` navigation. Code that read `GroupId` directly has to move to one of
those; nothing else changes. A manifest scheduled without a group name still gets a group of
its own, named after its `externalId`. Naming a group and setting its `MaxActiveJobs`, `Priority`
and `IsEnabled` are covered in
[Per-Group Dispatch Controls](/docs/scheduler/scheduling-options#per-group-dispatch-controls).
The namespaces of the scheduler types involved are listed in the
[Namespace Reference](/docs/scheduler/setup#namespace-reference).

## Scaling Indexes (026)

`026_scaling_indexes.sql` adds partial and composite indexes that keep the hot queries off
sequential scans at high row counts. Every index is `CREATE INDEX IF NOT EXISTS`, so it is safe
to re-run.

| Index | Table | Covers |
|-------|-------|--------|
| `ix_metadata_train_state_start_time` | `metadata` | Stale and stuck job queries (partial: `pending`, `in_progress` only) |
| `ix_metadata_start_time_desc` | `metadata` | Dashboard KPI aggregations, API pagination |
| `ix_metadata_manifest_id_train_state` | `metadata` | Active job counts per manifest group (partial: `pending`, `in_progress` only) |
| `ix_metadata_end_time_desc` | `metadata` | Health check failure counts (partial: non-null `end_time` only) |
| `ix_background_job_unfetched` | `background_job` | Worker dequeue query (partial: unfetched only) |
| `ix_work_queue_manifest_id_status_queued` | `work_queue` | Dormant dependent activation check (partial: `queued` only) |

They matter once `metadata` holds more than a few thousand rows. They change query performance,
not results.

## Foreign-key and Manifest Indexes (036)

`036_fk_and_manifest_eval_indexes.sql` (Postgres) or `004_fk_and_manifest_eval_indexes.sql`
(SQLite). Every index is `CREATE INDEX IF NOT EXISTS`, so it is safe to re-run.

| Index | Table | Covers |
|-------|-------|--------|
| `ix_metadata_parent_id` | `metadata` | Parent back-reference check when deleting metadata (partial: non-null only) |
| `ix_work_queue_metadata_id` | `work_queue` | Metadata back-reference check when deleting metadata (partial: non-null only) |
| `ix_dead_letter_retry_metadata_id` | `dead_letter` | Retry back-reference check when deleting metadata (partial: non-null only) |
| `ix_metadata_manifest_failed` | `metadata` | Per-manifest `FailedCount` in the dispatch loop (partial: `failed` only) |

PostgreSQL does not index the referencing side of a foreign key on its own. Without the first
three, the `ON DELETE RESTRICT` checks behind `DeleteExpiredMetadataJunction`'s cleanup DELETE
scanned those tables, so the cleanup grew with the size of the database. The fourth bounds the
per-manifest failed-count subquery in `LoadManifestsJunction` to failed rows instead of the
manifest's entire terminal history.

## Queued Subject Index (046)

`046_work_queue_subject_queued_index.sql` (Postgres) or `011_work_queue_subject_queued_index.sql`
(SQLite) adds one partial index. It is `CREATE INDEX IF NOT EXISTS` and safe to re-run. On Postgres
it is built `CONCURRENTLY` where it has not been built yet, so enqueue and dispatch are not blocked
while it builds; a database that already ran 046 is not touched again.

| Index | Table | Covers |
|-------|-------|--------|
| `ix_work_queue_subject_queued` | `work_queue` | The "queued behind" lookup on a work queue entry's detail, in the API and the dashboard (partial: `queued` with a non-null `subject_key`) |

The two subject indexes from migration 042 cover dispatched rows, so before this the lookup read
every queued row and filtered on the key. With 500,000 queued entries that took about 137 ms per
detail view; through this index it takes about 1 ms. Entries without a subject key, which is most
manifest work, are left out of the index, so it stays small.

## State-machine Request Scope (048)

`048_snapshot_draft_request_scope.sql` (Postgres) or `013_snapshot_draft_request_scope.sql`
(SQLite) adds two nullable columns to `snapshot_draft`: `last_request_trigger` and
`last_request_from_state`. The Postgres statements are `ADD COLUMN IF NOT EXISTS` and safe to
re-run. Nothing is backfilled.

A draft now records the trigger and from-state of its last request id, and replays a retry only
for the same trigger
([how a request id is matched](/docs/sdk-reference/statemachine-api/persistence-ports#how-a-request-id-is-matched)).
A draft written before the migration has no recorded trigger, so a retry of its last request is
refused once as `request-id-reused` rather than replayed. A custom `ISnapshotStore` should
override `UpdateWithRequest` to store the whole request; without it, every retry against that
store is refused the same way.

## Timestamps as timestamptz (049)

`049_timestamptz_columns.sql` (Postgres only) changes five columns from `timestamp without time zone`
to `timestamptz`: `work_queue.created_at` and `dispatched_at`, `manifest_group.created_at` and
`updated_at`, and `manifest.next_scheduled_run`. Their defaults, and `background_job.created_at`'s,
become `now()`.

A plain timestamp stores the writing session's wall-clock time, so a host whose Postgres session was
not in UTC stored these hours off: `CreatedAt` read back four or five hours early from New York, and
variance schedules fired early or late. Each stored value is read as UTC, which is what every host
with a UTC session wrote. A row written from another zone before the upgrade was shifted when it was
stored, and keeps that shift.

**Plan a maintenance window on a large database.** Each `ALTER` rewrites its table under an
`ACCESS EXCLUSIVE` lock. `work_queue` is the one that matters: it keeps dispatched rows until metadata
cleanup removes them, so on a large queue the rewrite blocks enqueue and dispatch, on every host, for
as long as it takes. Stop the scheduler (or every host) first, or run the migration yourself with
[SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) at a quiet time. Each change checks
the column's type first, so an interrupted run resumes where it stopped.

## External Id Index (050)

`050_metadata_external_id_index.sql` (Postgres only) adds `ix_metadata_external_id`, a plain btree index
on `trax.metadata (external_id)`. It is not unique: a dispatch retry adds a run under the same id.

External id is the one key that is the same from enqueue to run, so a consumer correlating its own
records with runs looks them up by it. Without an index each lookup read the whole table: 48 to 77 ms
at a million rows, against 0.05 ms with it. `external_id` is `char(32)`, so compare a `char(32)` to use
it without a cast.

It is built `CREATE INDEX CONCURRENTLY`, so runs keep being written while it builds, and the build
takes as long as the table is large. It waits for transactions already open on `metadata` to finish
before it starts, so a long transaction on a running instance delays startup of the upgraded one.

## SQLite enum partial indexes (014)

`014_enum_integer_partial_indexes.sql` (SQLite only) recreates eleven partial indexes on `work_queue`,
`metadata` and `dead_letter`. Their predicates compared the enum columns with Postgres labels
(`status = 'queued'`), but SQLite stores an enum as its integer, so the indexes covered no row: the
unique one-queued-entry-per-manifest index enforced nothing, and the others served no query. They now
compare integers.

Because that unique index was inert, a SQLite database can hold two queued entries for one manifest,
and the index cannot be built over them. Before rebuilding it, 014 keeps each manifest's oldest queued
entry and cancels the rest (status `Cancelled`), which is what the queue would have held had the index
worked.
