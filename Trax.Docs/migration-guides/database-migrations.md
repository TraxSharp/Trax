---
layout: default
title: Database Migrations
description: The Postgres and SQLite migrations that change something visible, such as columns, indexes or row behaviour, and what each one means for an upgrade.
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

On Postgres each script waits at most five seconds for a table lock, so a migration behind a long
transaction on a running instance does not stall enqueue and dispatch on every host. A script that
gives up is run again, up to ten times, and then startup fails with `55P03`
([why](/docs/reference/writing-migrations#every-postgres-script-can-run-again)).

**Upgrading a scheduler from Trax.Effect 1.57.2 or earlier:** stop every scheduler host before
starting one on the new version. The leader-lock key changed in 1.57.3, so an old host and a new one
would both run the ManifestManager
([details](/docs/scheduler/concurrency)).

## ManifestGroup (014)

`014_manifest_group.sql` promotes a manifest's group from a denormalized string (`group_id`) to
its own table with per-group dispatch controls. It:

1. creates `trax.manifest_group` with `name`, `max_active_jobs`, `priority`, `is_enabled` and
   timestamp columns;
2. seeds a `manifest_group` row for each distinct `group_id` on existing manifests, and one per
   ungrouped manifest, named after its `external_id`;
3. adds a NOT NULL `manifest_group_id` foreign key to `trax.manifest`;
4. drops the old `group_id` column.

Existing manifests keep their groups. It is not idempotent: it predates the
[rule that every script can run again](/docs/reference/writing-migrations#every-postgres-script-can-run-again),
and its `CREATE TABLE`, `ADD COLUMN` and `CREATE INDEX` carry no `IF NOT EXISTS`. The migrator
journals it and never runs it twice, but if it stops partway (a killed process, a failed
statement) the next start runs it again from the top and fails on the table it already created.
Repair that by hand: finish or undo the partial change, then start again.

`Manifest.GroupId` (`string?`) is replaced by `Manifest.ManifestGroupId` (`long`, since `018_bigint_ids.sql`) and the
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

It does not rewrite the tables. Each block sets its own transaction's time zone to UTC and changes the
type with no `USING`, and in a UTC session Postgres reads each stored value as UTC and keeps the
table's storage as it is, so the `ACCESS EXCLUSIVE` lock each `ALTER` takes lasts for a catalog change,
not a copy of the table. That holds even when the scripts run from a session in another zone. The lock
still has to be granted, so it waits for transactions already open on those tables, at most five
seconds per try (see the top of this page). Each change checks the column's type first, so an
interrupted run resumes where it stopped.

## External Id Index (050)

`050_metadata_external_id_index.sql` (Postgres only) adds `ix_metadata_external_id`, a plain btree index
on `trax.metadata (external_id)`. It is not unique: a dispatch retry adds a run under the same id.

External id is the one key that is the same from enqueue to run, so a consumer correlating its own
records with runs looks them up by it. Without an index each lookup read the whole table: 48 to 77 ms
at a million rows, against 0.05 ms with it. `external_id` is `char(32)`, so compare a `char(32)` to use
it without a cast. The data context maps the column as `character(32)`, so a LINQ comparison such as
`Where(m => m.ExternalId == id)` sends a `char(32)` parameter and uses the index; before that mapping
it sent text, which made Postgres cast the column and read the whole table.

It is built `CREATE INDEX CONCURRENTLY`, so runs keep being written while it builds, and the build
takes as long as the table is large. It waits for transactions already open on `metadata` to finish
before it starts, so a long transaction on a running instance delays startup of the upgraded one, and
one that stays open for about a minute fails it with `55P03` until that transaction ends.

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

## SQLite fixed-width offset timestamps (017)

`017_fixed_width_offset_timestamps.sql` (SQLite only) rewrites `effect_claim.lease_expires_at` and
`created_at` and `snapshot_draft.updated_at` into the fixed-width UTC text Trax now writes
(`2026-09-29 12:00:00.1200000+00:00`). SQLite compares these columns as text, and a row written before
the fixed-width form held EF's default text, with the shortest fraction and the value's own offset. At
the exact instant it compared as earlier than itself, so a lease or a draft's age read one tick early,
and a non-UTC offset sorted wrong outright. The whole seconds are converted to UTC and the fraction is
padded, so no precision is lost. Rows already in the fixed-width form are not touched, and a second
run changes nothing.

## Effect claim content fingerprint (053, SQLite 018)

`053_effect_claim_content_fingerprint.sql` (Postgres) and `018_effect_claim_content_fingerprint.sql` (SQLite) add a
nullable `content_fingerprint` text column to `effect_claim`. The state-machine effect runner records the SHA-256 of
the draft's canonical wire there when it claims the effect, and replays the claim's receipt only onto a draft with
the same content; see [effects](/docs/sdk-reference/statemachine-api/effects#exactly-once-and-the-receipt).

Nothing is backfilled. A claim written before the upgrade has no fingerprint and replays as it did, and a host still
on the previous version reads and writes the table unchanged, so a rolling deploy is safe. Adding a nullable column
is a catalog change on both providers, not a rewrite of the table.

## Junction runs (055, 057, 058, 060, 067 and 068; SQLite 020, 022, 023, 025, 030 and 031)

`055_junction_run.sql` (SQLite `020_junction_run.sql`) creates `trax.junction_run`, one row per step
of a run, written only by a host that calls
[`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events). Its `metadata_id`
foreign key cascades, so every delete of metadata (cleanup, manifest pruning) removes a run's steps
with it. On Postgres it also creates the `trax.junction_run_kind` and `trax.junction_run_state`
enum types. `057_junction_run_attempt.sql` (SQLite `022`) adds the nullable `attempt` column, and
`060_junction_run_track.sql` (SQLite `025`) adds `name_withheld`, true for a step whose name is
withheld after a `[TraxSensitive]` route, and the nullable `track_position`, the route a step ran
after. `067_junction_run_node_id.sql` (SQLite `030`) adds the nullable `node_id`, the id of the
declared node a step ran for; rows written before it have none. `068_parallel_branch_path.sql`
(SQLite `031`) adds the nullable `branch_path`, the `Parallel` branch a step ran in, described
[below](#parallel-branch-path-068-sqlite-031).

`058_metadata_manifest_id_id_index.sql` (SQLite `023`) adds `ix_metadata_manifest_id_id` on
`trax.metadata (manifest_id, id DESC)` for rows with a manifest, so a run reads its attempt from
the manifest's latest runs instead of sorting all of them. On Postgres it is built `CONCURRENTLY`,
so it does not block writes to metadata, but on a large table it takes a while to build.

A host on the previous version never writes the table, so a rolling deploy is safe.

## Manifest replay on retry (056, SQLite 021)

`056_manifest_replay_decisions_on_retry.sql` (SQLite `021`) adds
`trax.manifest.replay_decisions_on_retry`, a boolean that defaults to true. Every existing manifest
therefore replays the failed run's decisions on its retries and dead-letter requeues, under the
checks in [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions).
Set it false with [`ReplayDecisionsOnRetry(false)`](/docs/sdk-reference/scheduler-api/schedule#scheduleoptions)
for a manifest whose retries should ask afresh.

## Decision state hash (059, SQLite 024)

`059_decision_state_hash.sql` (SQLite `024`) adds the nullable `state_hash` column to
`trax.decision`: the hash of the state each recorded question was asked about. A replay hands it
back, and a recorded answer is replayed only into a state that hashes the same. The value is `k1:`
and the hex of an HMAC-SHA256 when a [state hash key](/docs/effect/decisions#keying-the-state-hash)
is configured, or `s1:` and the hex of a SHA-256 without one. It is null when the state could not be
encoded or, without a key, when the state can hold a `[TraxSensitive]` value.

Nothing is backfilled. An answer recorded before the upgrade has no hash, so it is not replayed:
the first requeue or retry of a run recorded before the upgrade asks its deciders afresh. Adding a
nullable column is a catalog change on both providers, not a rewrite of the table.

## Abandoned replays and one queued replay per run (061 and 062, SQLite 026 and 027)

`061_metadata_replay_abandoned.sql` (SQLite `026`) adds `trax.metadata.replay_abandoned`, a boolean
that defaults to false. It marks a manifest's retry that named a run to replay and asked its deciders
afresh because the replay could not be honoured, so a later replay of that run stops there; see
[A requeue of a requeue](/docs/effect/decisions#a-requeue-of-a-requeue).

`062_work_queue_unique_queued_replay.sql` (SQLite `027`) adds the unique partial index
`ix_work_queue_unique_queued_replay` on `trax.work_queue (replay_decisions_of)` for queued entries
that name a run to replay, so at most one queued entry replays a given run. Before building it, the
migration clears `replay_decisions_of` on every queued entry that duplicates an older one's, keeping
the oldest: those entries still run, and ask their deciders afresh. On Postgres the index is built
`CONCURRENTLY`, so enqueue and dispatch keep writing while it builds. When a duplicate written
meanwhile fails the build, the migrator drops the invalid index and runs `062` again, so its
clean-up resolves the duplicate before the next build.

Nothing is backfilled in `061`, and a host on the previous version never reads the column, so a
rolling deploy is safe.

## Log text search (063)

`063_log_text_search_trigram.sql` (Postgres only) installs the `pg_trgm` extension and adds two GIN
trigram indexes on `trax.log`: `ix_log_message_trgm` over `lower(message)` and `ix_log_category_trgm`
over `lower(category)`. They serve the scheduler's log text filters
([`MessageContains` and `CategoryContains`](/docs/sdk-reference/scheduler-api/i-operations-service#logquery)),
which the dashboard's log grids and the GraphQL `logs` query share. Without them a term almost no
entry carries read the whole table: a page took about 390 ms at three million rows, and takes about
3 ms through the index. A term most entries carry still reads by id, as it did, and a term shorter than
three characters has no trigram to look up, so it still reads the table.

`pg_trgm` ships with Postgres's contrib modules in every mainstream distribution and managed service,
and since Postgres 13 it is a trusted extension, so a role with `CREATE` on the database installs it
without being a superuser. If the role Trax migrates with cannot, run `CREATE EXTENSION pg_trgm;` once
as one that can, before upgrading; the migration then finds it. The extension is created in the first
schema on the session's search path (normally `public`) and the operator class is looked up through
the same path, so a `pg_trgm` already installed in a schema off that path needs the schema added to
the connection string's `Search Path`.

The indexes are built `CREATE INDEX CONCURRENTLY`, so log writes carry on while they build, but the
build is the cost of this upgrade on a large log table: each reads the whole table, about 23 s for the
message index and 11 s for the category index at three million short entries, longer with longer
messages. Startup of the upgraded host waits for it. Together they take about 60% of the table's size
on disk, and every log write after the upgrade updates both. To build them ahead of the upgrade, run
the script's statements by hand; the migration skips indexes that already exist under those names.

SQLite has no trigram index, so on SQLite a text filter still reads the table.

## Decision page index (064, SQLite 028)

`064_decision_metadata_id_id_index.sql` (SQLite `028`) adds `ix_decision_metadata_id_id` on
`trax.decision (metadata_id, id)`. A run's recorded decisions are read a page at a time in id order,
and the only index leading on `metadata_id` (`uq_decision_run_question`) orders by question, so
Postgres walked the primary key and skipped every other run's decisions to reach the run's first: about
290 ms for that page at a million decisions, growing with the table, and 2 ms with the index. On
Postgres it is built `CONCURRENTLY`, so decisions keep being recorded while it builds.

## Log metadata id not null (065, SQLite 029)

`065_log_metadata_id_not_null.sql` (SQLite `029`) makes `trax.log.metadata_id` `NOT NULL` with a
default of 0, after turning any NULL already there into 0. The model has always read the column as a
plain number and the log writer always stores 0, so a row written without it, by hand or by another
tool, held NULL and broke every read of a log page that contained it. Now such a row reads back with
0, like the writer's own.

On Postgres the migration proves the column has no NULLs with a check constraint added `NOT VALID`
and then validated, which reads the table without blocking log writes, so `SET NOT NULL` takes its
exclusive lock only briefly; the check is dropped afterwards. SQLite cannot change a column in place,
so `029` rebuilds the `log` table with the same columns, ids and index.

## Parallel branch path (068, SQLite 031)

`068_parallel_branch_path.sql` (SQLite `031`) records which `Parallel` branch each decision was asked
in and each step ran in. A branch counts its askings of a question on from where it forked, so two
branches asking one question ask it under the same `occurrence`, and the old key
`uq_decision_run_question` on `(metadata_id, question_key, occurrence)` refused the second. The
migration adds `trax.decision.branch_path`, `text NOT NULL DEFAULT ''`, empty for a question asked
outside any branch, builds the unique index `uq_decision_run_branch_question` on
`(metadata_id, branch_path, question_key, occurrence)` and then drops the old constraint. A requeue
replays an answer by branch, key and occurrence. It also adds the nullable
`trax.junction_run.branch_path`.

Every existing row is a decision asked outside any branch, so nothing is backfilled. On Postgres the
new index is built `CONCURRENTLY`, so decisions keep being recorded while it builds, and the column
default is a catalog change. SQLite cannot drop a table's unique constraint in place, so `031`
rebuilds the `decision` table with the same columns, ids and index. A host on the previous version
inserts without the column and gets the empty branch, so a rolling deploy is safe.

## State-machine instance listing (071, SQLite 033)

`071_snapshot_draft_operator_listing.sql` (SQLite `033`) serves the operator's list of state-machine
instances (the API's [`machineInstances`](/docs/sdk-reference/graphql-api/queries#machineinstances)
and the dashboard's [State Machines page](/docs/dashboard#state-machines)), which reads newest first
by `updated_at`. It adds the nullable `trax.snapshot_draft.created_at`, which the store writes when it
creates a row; a row created before the migration, or by a host still on the previous version, reads
it as null, since nothing recorded when it began. It replaces the `(machine, state)` index with
`ix_snapshot_draft_machine_state_updated` on `(machine, state, updated_at DESC, row_id DESC)`, which
on Postgres includes `owner_kind` so the counts by state read the index alone, and adds
`ix_snapshot_draft_updated` on `(updated_at DESC, row_id DESC)` for a list with no state filter. On
Postgres both are built `CONCURRENTLY`, so drafts keep being saved while they build. At two million
instances every page measured took under 10 ms.

## Checkpoints and resume links (074, SQLite 036)

`074_checkpoint.sql` (SQLite `036_checkpoint.sql`) creates `trax.checkpoint`, one row per run and
declared `Checkpoint<TState>()` node: the run's `metadata_id`, the `node_id` and `branch_path` it was
taken at, the declared state as JSON in `state` with its readable `state_type`, the `tracks` the
routing steps before it took, and the `chain_hash` and `state_fingerprint` a resume compares with the
running code. A unique key on `(metadata_id, node_id)` keeps one row per node. Its `metadata_id`
foreign key cascades, so every delete of metadata removes a run's checkpoints with it.

It also adds the nullable `resume_from` and `resume_at` to `trax.work_queue` and `trax.metadata`: the
run an entry resumes and the node it resumes at, null for after that run's latest checkpoint. Like
`replay_decisions_of`, `resume_from` is not a foreign key. The unique partial index
`ix_work_queue_unique_queued_resume` on `trax.work_queue (resume_from)` for queued entries allows one
queued resume per run, and the partial `ix_metadata_resume_from` serves the cleanup's lookup of the
runs that resume another. On Postgres both are built `CONCURRENTLY`, so enqueue, dispatch and run
writes carry on while they build.

The columns are new and nothing is backfilled. A host on the previous version never reads or writes
them, so a rolling deploy is safe. The model, `IDataContext.Checkpoints`, and the new properties are
experimental (`TRAXEXP003`).

## Failure search (066)

`066_metadata_failure_search.sql` (Postgres only) adds two indexes on `trax.metadata` for the GraphQL
[`executions`](/docs/sdk-reference/graphql-api/queries#executions) filters `failureReasonContains`
and `failureJunction`. `ix_metadata_failure_reason_trgm` is a GIN trigram index over
`lower(failure_reason)`, the same kind as `063`'s log indexes, and serves a search for text inside a
run's failure reason. `ix_metadata_failure_junction` is a partial B-tree on
`(failure_junction, id)` that holds only runs with a failure junction, and serves an exact match on
the junction in id order. Without them a term or junction few runs carry, and the count of any
match, read the whole run table.

`pg_trgm` is installed by `063`, so this migration needs nothing more from the role Trax migrates
with; its own `CREATE EXTENSION IF NOT EXISTS` is a no-op after `063`.

Both are built `CREATE INDEX CONCURRENTLY`, so runs keep being written while they build, but each
reads the whole run table. At three million runs, two in nine of them failed with a reason of about
fifty characters, the trigram index took about 7 s and 50 MB, and the junction index under a second
and 30 MB. The trigram index grows with how many runs failed and how long their reasons are. Startup
of the upgraded host waits for the build; to build them ahead of the upgrade, run the script's
statements by hand, and the migration skips indexes that already exist under those names.

SQLite has no trigram index and gets no migration here, so on SQLite both filters read the table.
