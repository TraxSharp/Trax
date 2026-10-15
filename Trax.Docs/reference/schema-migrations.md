---
title: Schema migrations
description: "The Postgres and SQLite migrations that change something visible, such as columns, indexes or row behaviour, in number order with both providers paired."
type: reference
area: data
nav_order: 2
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations
  - Trax.Effect/src/Trax.Effect.Data.Sqlite/Migrations
redirect_from:
  - /docs/migration-guides/database-migrations
---

# Schema migrations

`UsePostgres` and `UseSqlite` apply every pending migration at startup. This page lists the
migrations that change something a host can see: a column its code reads, an index that matters
on a large database, or how an existing row behaves after the upgrade. A host that calls
[SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) runs them itself before the
new version serves traffic.

Postgres and SQLite number their migrations independently. Sections are in Postgres number order;
a heading names the Postgres script first and the SQLite one after the word SQLite, and a section
for one provider only says so. The two SQLite-only sections sit where their numbers fall between
the paired ones.

On Postgres each script waits at most five seconds for a table lock, so a migration behind a long
transaction on a running instance does not stall enqueue and dispatch on every host. A script that
gives up is run again, up to ten times, and then startup fails with `55P03`
([why](/docs/contributing/writing-migrations#every-postgres-script-can-run-again)). Scripts run
without a transaction, so a script that stops partway keeps the statements it ran and runs again
from the top at the next start.

Procedures that go with a migration:

- [Repair a partial migration 014](/docs/migration-guides/repair-partial-migration-014)
- [Install pg_trgm before migration 063](/docs/migration-guides/install-pg-trgm)
- [Turn off state-machine change notices](/docs/migration-guides/turn-off-snapshot-change-notices) (080)

A scheduler upgraded from Trax.Effect 1.57.2 or earlier also needs every old host stopped first:
see [Upgrade schedulers from Trax.Effect 1.57.2 or earlier](/docs/migration-guides/scheduler-leader-lock).

## Manifest groups (014)

`014_manifest_group.sql` promotes a manifest's group from a denormalized string (`group_id`) to
its own table with per-group dispatch controls. It:

1. creates `trax.manifest_group` with `name` (unique), `max_active_jobs`, `priority`, `is_enabled`
   and timestamp columns;
2. seeds a `manifest_group` row for each distinct `group_id` on existing manifests, and one per
   ungrouped manifest, named after its `external_id`;
3. sets each group's `priority` to the highest `priority` among its manifests, where that is above 0;
4. adds a NOT NULL `manifest_group_id` foreign key to `trax.manifest`, with the index
   `ix_manifest_manifest_group_id`;
5. drops the old `group_id` column and its index.

Existing manifests keep their groups. It is not idempotent: it predates the
[rule that every script can run again](/docs/contributing/writing-migrations#every-postgres-script-can-run-again),
and its `CREATE TABLE`, `ADD COLUMN` and `CREATE INDEX` carry no `IF NOT EXISTS`. The migrator
journals it and never runs it twice, but if it stops partway (a killed process, a failed
statement) the next start runs it again from the top and fails on the table it already created.
[Repair a partial migration 014](/docs/migration-guides/repair-partial-migration-014) undoes or
records the partial change.

`Manifest.GroupId` (`string?`) is replaced by `Manifest.ManifestGroupId` (`long`, since `018_bigint_ids.sql`) and the
`Manifest.ManifestGroup` navigation. Code that read `GroupId` directly has to move to one of
those; nothing else changes. A manifest scheduled without a group name still gets a group of
its own, named after its `externalId`. Naming a group and setting its `MaxActiveJobs`, `Priority`
and `IsEnabled` are covered in
[Per-Group Dispatch Controls](/docs/scheduler/scheduling-options#per-group-dispatch-controls).
The namespaces of the scheduler types involved are listed in the
[Namespace Reference](/docs/scheduler/namespaces).

## Scaling indexes (026)

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

## Foreign-key and manifest indexes (036, SQLite 004)

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

## Queued subject index (046, SQLite 011)

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

## State-machine request scope (048, SQLite 013)

`048_snapshot_draft_request_scope.sql` (Postgres) or `013_snapshot_draft_request_scope.sql`
(SQLite) adds two nullable columns to `snapshot_draft`: `last_request_trigger` and
`last_request_from_state`. The Postgres statements are `ADD COLUMN IF NOT EXISTS` and safe to
re-run. Nothing is backfilled.

A draft now records the trigger and from-state of its last request id, and replays a retry only
for the same trigger
([how a request id is matched](/docs/sdk-reference/statemachine-api/persistence-ports#how-a-request-id-is-matched)).
A draft written before the migration has no recorded trigger, so a retry of its last request is
refused once as `REQUEST_ID_REUSED` rather than replayed. A custom `ISnapshotStore` should
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

## External id index (050)

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
the same content; see [effects](/docs/sdk-reference/statemachine-api/i-snapshot-effect#exactly-once-and-the-receipt).

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
checks in [Retries replay decisions](/docs/scheduler/how-retries-work#retries-replay-decisions).
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
without being a superuser. The script runs `CREATE EXTENSION IF NOT EXISTS pg_trgm`, which fails when the role Trax migrates
with may not create it. The extension is created in the first schema on the session's search path
(normally `public`). One already installed in another schema is found too: the migrator adds that
schema to the search path of the session that runs the scripts. See
[Install pg_trgm before migration 063](/docs/migration-guides/install-pg-trgm).

The indexes are built `CREATE INDEX CONCURRENTLY`, so log writes carry on while they build, but the
build is the cost of this upgrade on a large log table: each reads the whole table, about 23 s for the
message index and 11 s for the category index at three million short entries, longer with longer
messages. Startup of the upgraded host waits for it. Together they take about 60% of the table's size
on disk, and every log write after the upgrade updates both. The migration skips an index that already
exists under its name, so the indexes can be built ahead of the upgrade by running the script's
statements by hand.

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

## Failure search (066)

`066_metadata_failure_search.sql` (Postgres only) adds two indexes on `trax.metadata` for the GraphQL
[`executions`](/docs/sdk-reference/graphql-api/queries/executions#executions) filters `failureReasonContains`
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

## Queued-work notify (069)

`069_work_queue_notify.sql` (Postgres only) adds two triggers on `trax.work_queue` that send
`pg_notify('trax_queued_work', '')` when work becomes dispatchable: `work_queue_inserted_notify`,
once per inserting statement, and `work_queue_confirmed_notify`, when a staged entry is confirmed.
The job dispatcher on every host listens on that channel through
[`IQueuedWorkListener`](/docs/sdk-reference/configuration/i-queued-work-listener) and wakes at once
instead of at its next poll. A notice is sent when the writing transaction commits and never when it
rolls back, and identical notices in one transaction are folded into one. The poll stays the
guarantee: a host on the previous version, or one whose listener is reconnecting, still dispatches
at its usual interval. SQLite gets a wake within the process instead, so it has no migration.

## State-machine owner kind and invoke token (070, SQLite 032)

`070_snapshot_draft_owner_kind.sql` (SQLite `032`) prepares `trax.snapshot_draft` for
[system-owned instances and invoked trains](/docs/statemachine/invoking-trains):

- `owner_kind`, the new enum `trax.snapshot_owner_kind` (`user` or `system`; on SQLite the integer of
  `SnapshotOwnerKind`, 0 or 1). Every existing row is a user's, so it defaults to `user`. A system
  row has no `user_key`, so `user_key` becomes nullable.
- `invoke_token`, the external id of the run the instance's current state invoked, unique where set
  and never part of the snapshot a client sees.
- `row_id`, a surrogate primary key, because a nullable `user_key` cannot stay in the key. Identity
  is now two partial unique indexes: `(user_key, machine, id)` among user rows, the key every row
  had before, and `(machine, id)` among system rows.

On Postgres nothing rewrites the table or holds it locked for a scan, so drafts keep being saved
while it runs: `owner_kind`'s default stays in the catalog, `row_id` is filled for existing rows in
batches of 10,000 that each commit, `NOT NULL` and the owner check are validated under a lock that
lets writes continue, the indexes are built `CONCURRENTLY`, and the swap to the new key is one short
transaction that reads no rows. A script that stops partway runs again from the top. SQLite cannot
drop a key in place, so there the table is rebuilt and every row copied across as a user's.

A host on the previous version keeps working during a rolling deploy: it inserts without
`owner_kind` or `row_id`, which both default, and its reads name a `user_key`, which a system row
never matches.

## State-machine instance listing (071, SQLite 033)

`071_snapshot_draft_operator_listing.sql` (SQLite `033`) serves the operator's list of state-machine
instances (the API's [`machineInstances`](/docs/sdk-reference/graphql-api/queries/machine-instances#machineinstances)
and the dashboard's [State Machines page](/docs/dashboard/pages#state-machines)), which reads newest first
by `updated_at`. It adds the nullable `trax.snapshot_draft.created_at`, which the store writes when it
creates a row; a row created before the migration, or by a host still on the previous version, reads
it as null, since nothing recorded when it began. It replaces the `(machine, state)` index with
`ix_snapshot_draft_machine_state_updated` on `(machine, state, updated_at DESC, row_id DESC)`, which
on Postgres includes `owner_kind` so the counts by state read the index alone, and adds
`ix_snapshot_draft_updated` on `(updated_at DESC, row_id DESC)` for a list with no state filter. On
Postgres both are built `CONCURRENTLY`, so drafts keep being saved while they build. At two million
instances every page measured took under 10 ms.

## Invoked-run link (072, SQLite 034)

`072_invoked_run_link.sql` (SQLite `034`) adds the nullable `invoking_machine`,
`invoking_instance_id` and `invoking_owner_kind` to `trax.work_queue` and `trax.metadata`: the
machine, instance and owner kind whose invoking state queued the run. The enqueue writes them on the
entry and the dispatcher copies them to the run, so the link outlives the instance's
`invoke_token`. They are null on every other run. `invoking_owner_kind` reuses
`trax.snapshot_owner_kind` from 070. The partial index `ix_metadata_invoking_instance` on
`(invoking_machine, invoking_instance_id)` serves the operator's list of an instance's runs; on
Postgres it is built `CONCURRENTLY`.

A run carrying the link cannot be requeued: the machine retries it by entering its state again.

## Invoked-run outcome (073, SQLite 035)

`073_invoked_run_outcome.sql` (SQLite `035`) adds `trax.metadata.invoke_output`, the run's output
as the machine's `OnDone` edges read it, and `invoke_output_oversize`, set instead when the output is
past the 64 KiB snapshot cap (the invoking state then fails). The run writes both in the same update
that records it completed, so a host applying the outcome after a crash finds it there. They are
null and false on every other run, are never shown on an operator surface, and are separate from
`output`, which is redacted and bounded by the host's policy.

On Postgres it also adds triggers that send `pg_notify('trax_invoked_run_ended', <external id>)`
when an invoked run ends (its metadata is updated or inserted as completed, failed or cancelled, or
its still-queued entry is cancelled), so the state machine's outcome reconciler on every host
applies the outcome at once. Its sweep stays the guarantee; on SQLite, which has no notification
channel, the run's own host applies the outcome and the sweep covers the rest.

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
them, so a rolling deploy is safe.

## Stranded invoking state (075, SQLite 037)

`075_snapshot_draft_invoke_stranded_state.sql` (SQLite `037`) adds the nullable
`trax.snapshot_draft.invoke_stranded_state`: the invoking state an instance was left in when a run
it invoked ended and not even its failure could be applied. The update that clears the token writes
it, and a write that gives the row a token or moves it to another state clears it, so an instance
stranded on purpose is told apart from one that lost its token. It is server-only and added without
a default, so the table is not rewritten.

## State-machine revision (076, SQLite 038)

`076_snapshot_draft_revision.sql` (SQLite `038`) adds `trax.snapshot_draft.revision`, a
`bigint not null default 1`, and the nullable `ir_hash`. A row starts at revision 1 and a trigger
named `snapshot_draft_revision` adds one on every `UPDATE`: a `BEFORE UPDATE` trigger on Postgres
and an `AFTER UPDATE` one on SQLite, which cannot assign to the new row. The database does the
bump so that every writer is numbered, a custom store, raw SQL and a host still on the previous
version included. On Postgres a value a writer puts in the column is overwritten. On SQLite an
update that leaves the column where it was or moves it back ends one past the stored revision, and
one that moves it forward keeps its value, so the column never goes back; the trigger's own write is
skipped by its `WHEN` clause, so it stops there even with `PRAGMA recursive_triggers` on. `ir_hash` is the IR hash
(`IMachine.SchemaHash`) of the definition that wrote the row, null for a machine with no
exportable IR. Both are server-only and never part of a snapshot.

Existing rows read revision 1 and keep their `row_id`. On Postgres the constant default stays in
the catalog, so the table is not rewritten. `row_id` comes from a sequence (Postgres) or
`AUTOINCREMENT` (SQLite) and is never reused, so a draft deleted and created again under the same
id is a new row with a new `row_id`, and its revisions start again at 1. Never empty the table with
`TRUNCATE ... RESTART IDENTITY` (or reset the SQLite sequence): a reused `row_id` would look to a
watching client like the incarnation it already holds, and a `TRUNCATE` fires no row trigger, so
no watcher is told the rows are gone.

A host on the previous version writes without the columns; its inserts take the default and its
updates are bumped by the trigger, so a rolling deploy is safe.

## State-machine start and request ring (077, SQLite 039)

`077_snapshot_draft_start_and_request_ring.sql` (SQLite `039`) adds three nullable columns to
`trax.snapshot_draft`: `start_request_id` and `start_fingerprint`, the request id and input hash of
the `start` that created the row, and `recent_requests` (`jsonb`, `TEXT` on SQLite), the row's last
sixteen client requests with the result each was answered with, at most 128 KiB of JSON text. A partial unique index,
`ux_snapshot_draft_start_request`, on `(owner_kind, user_key, machine, start_request_id)` where
`start_request_id` is set, makes a retried start find the instance it created. On Postgres it is built
`CONCURRENTLY`, so drafts keep being saved and advanced while it builds. All three are
server-only and never part of a snapshot.

They are added without defaults, so the table is not rewritten, and nothing is backfilled: an
existing row has no ring, and its last request is matched from the `last_request_*` columns as
before. A host on the previous version neither reads nor writes them; its advances leave the ring as
it is, so a rolling deploy is safe.

## State-machine change notify (078)

`078_snapshot_draft_change_notify.sql` (Postgres only) adds the trigger `snapshot_draft_changed_notify` on
`trax.snapshot_draft`, which sends `pg_notify('trax_snapshot_changed', <row_id>)` after every row it inserts,
updates or deletes: one notice per row, so a bulk delete sends one for each row it removes. The payload is the
row's `row_id` and nothing else. It is a hint: the state-machine change listener on each host re-reads the row's
revision, state and existence before it pushes anything, so a notice sent by hand moves nothing and no context
travels through the notification queue. A notice is sent when the writing transaction commits and never when it
rolls back, and identical notices in one transaction are folded into one, so two writes to one row in one
transaction send one.

A trigger rather than a call at each write site covers every writer: Trax's own paths, the outcome hook and
reconciler, the draft sweep, a host's custom store, raw SQL, and a host still on the previous version during a
rolling deploy. The function is replaced and the trigger created only when it is missing, so the script runs again
over its own result. SQLite has no notification channel, so it has no migration: there the listener polls the
`(row_id, revision)` pairs of the watched rows instead, and a Postgres host with `ChangeFeed = Poll` polls the same
way.

## State-machine timers (079, SQLite 040)

`079_snapshot_draft_due_at.sql` (SQLite `040`) adds the nullable `due_at` (`timestamptz`, fixed-width UTC text on
SQLite) to `trax.snapshot_draft`: when the timer of the state the instance is in fires, or null when that state has no
timer. Every write that enters a timed state sets it from the database's clock in the same statement as the state; a
write that leaves the state clears it. A partial index, `ix_snapshot_draft_due` on `(due_at, row_id)` where `due_at` is
set, serves the sweep that fires due timers. On Postgres it is built `CONCURRENTLY`, so drafts keep being written while
it builds. The column is server-only and never part of a snapshot.

It is added without a default, so the table is not rewritten, and nothing is backfilled: an existing row in a timed
state has no due time until it is entered again. A host on the previous version writes without the column, so a row it
moves keeps whatever due time it had. During a rolling deploy that has two effects. A row it moves into a state with no
timer keeps the old due time until a sweep finds it and clears it, without firing. A row it moves from one timed state
into another keeps the first state's due time, so the second state's timer fires at the first one's time, at once if
that time has passed, rather than its own duration after entry. A host on this version arms each timed state correctly
on entry. To avoid the gap, finish the deploy before any machine whose states chain timers is used, or drain such
instances first.

## State-machine change notify switch (080)

`080_snapshot_draft_change_notify_switch.sql` (Postgres only) recreates the trigger `snapshot_draft_changed_notify`
with a condition, `WHEN (lower(current_setting('trax.snapshot_notify', true)) IS DISTINCT FROM 'off')`; the function
`078` added is unchanged. `off` in any case turns the notices off. With the setting unset, `on`, or anything else,
every write notifies as before, so a database nobody configures behaves as it did under `078`. The script replaces
the trigger only while it has no condition, so it runs again over its own result.

The switch exists because every transaction that sends a Postgres `NOTIFY` takes one lock shared by the whole cluster
and holds it through its commit. With `078`'s trigger every write to `trax.snapshot_draft` is such a transaction, so
concurrent instance writes commit one at a time, together with the work queue's (`069`) and invoked runs' (`073`)
notices. A snapshot write in the same transaction as one of those notices still takes the lock for it.

The trigger reads the setting in the session that writes. A listening host can check only the database's own value
(`ALTER DATABASE <name> SET trax.snapshot_notify`, read from `pg_db_role_setting`), so that is the only supported
switch: a role setting (`ALTER ROLE ... SET`), a connection string's `Options=-c trax.snapshot_notify=off` or a
`SET LOCAL` turns the notices off for some writers only, and a host on `Notify` never hears their writes. A writer
whose session sets `session_replication_role = replica` fires no ordinary trigger and sends no notice either.

At startup, a host whose
[`StateMachineOptions.ChangeFeed`](/docs/sdk-reference/statemachine-api/add-state-machines#options) is `Notify`
refuses to start when the database's own setting turns the notices off, when a role's setting or its own session's
does, or when the trigger is missing or not enabled for ordinary sessions (`tgenabled` other than `O` or `A`, as after
`ALTER TABLE ... ENABLE REPLICA TRIGGER`). A `Poll` host whose database still notifies starts and logs a warning.
Either kind warns when its own session reads the setting otherwise than the database, or when the value is neither
`on` nor `off`. The check runs only at startup, so turning the notices off while a `Notify` host runs leaves its
watchers silent until it restarts. The order to turn the notices off in is in
[Turn off state-machine change notices](/docs/migration-guides/turn-off-snapshot-change-notices).

## State-machine pending effect and server record (081, SQLite 041)

`081_snapshot_draft_pending_effect_and_server_record.sql` (SQLite `041`) adds six nullable, server-only columns to
`trax.snapshot_draft`, none of them part of a snapshot:

- `effect_token` (`uuid`), `effect_key`, `effect_trigger`, `effect_request_id` and `effect_lease_until`
  (`timestamptz`): the effect pending on the row, the key it runs under (minted when the row is marked), its event, the
  client request that sent it and when the claimant's lease ends, on the database's clock. Sending an effect-bound event sets them by a conditional write; every other write to the row
  requires `effect_token` to be null, so nothing moves the row while its effect runs. The receipt, the move to the
  effect's target and clearing the columns are one write. Past the lease, the next send or the reconciler takes the
  mark over and runs the effect again under the same key.
- `server_record` (`boolean`): whether the server put the row in its state (an effect, an invoked train's outcome, a
  gated event), which decides what `discard` refuses, what the draft sweep keeps and what `MaxDraftsPerUser` counts.

A partial index, `ix_snapshot_draft_effect_pending` on `(effect_lease_until, row_id)` where `effect_token` is set,
serves the reconciler's read of marks whose lease has passed; on Postgres it is built `CONCURRENTLY`.

The columns are added without defaults and nothing is backfilled. A row with no `server_record` is judged by its state
as before: kept when its state is a committed state, an effect's target, an invoked train's outcome target or a gated
event's target. An effect claim the earlier version recorded in `trax.effect_claim` is still honoured: a completed one
whose row is still in the effect's state is recorded by that row's next send, which then removes it, and one in
flight within its lease keeps the row's events refused as `EFFECT_IN_PROGRESS`. A host on the previous version writes
without the columns, so during a rolling deploy it neither honours a mark a new host set nor sets one: finish the
deploy before an instance's effect is sent, or drain sends first.

## Run starter (082 and 083, SQLite 042)

`082_run_starter.sql` (SQLite `042`) adds a nullable `starter_hash` (`text`) to `trax.work_queue` and `trax.metadata`:
the lowercase hex SHA-256 of a tag and the id of whoever started the run, never the id itself. The stored form is
`SHA-256("p\0" + principalId)` for a caller, with the qualified principal id, and `SHA-256("u\0" + userKey)` for a
machine instance's owner, with the instance's user key, so a user key never matches a principal id that reads the
same. Who counts as the starter:

- A GraphQL train mutation records its caller, in `RUN` mode on the metadata row and in `QUEUE` mode on the work queue
  entry, which the dispatcher copies to the run's metadata.
- A state machine's invoking state records the instance's owner when a user owns it, and nothing for a system-owned
  instance.
- A requeue or resume from the operations surface keeps the original run's starter.
- A scheduled run, a run the dashboard or the operations namespace starts, a run on a remote runner and every run
  from before this migration record none.

Only the starter can follow a run through [`runProgress`](/docs/sdk-reference/graphql-api/run-progress), so a run
with no starter cannot be followed by anyone.

The invoked-run cap (`InvokedRunLimit`, see [Invoking a train](/docs/statemachine/invoking-trains#who-a-run-belongs-to)) counts a user's live invoked runs by
`starter_hash` as well. A row from before this migration, which has none, is still counted through its instance, so
the count does not drop during an upgrade. `083_metadata_live_invoked_starter_index.sql` adds the partial index that
read uses, `ix_metadata_live_invoked_starter` on `(starter_hash)` over the user-owned runs still pending or in
progress, built `CONCURRENTLY` on Postgres. SQLite `042` creates the same index.

Nothing is backfilled. A host on the previous version writes no starter, so a run it starts during a rolling deploy
cannot be followed, and is counted by the cap only through its instance.

## Manifest starter (084, SQLite 043)

`084_manifest_starter.sql` (SQLite `043`) adds a nullable `starter_hash` (`text`) to `trax.manifest`, the hash of a
principal id in the `p\0` form `082` uses. It is set when a manifest is scheduled with
[`ScheduleOptions.StartedBy`](/docs/sdk-reference/scheduler-api/schedule), and the scheduler copies it onto every work
queue entry it queues for the manifest, a retry, a trigger and a dead letter's requeue included, so the caller who
scheduled a one-off manifest can follow each of its runs through
[`runProgress`](/docs/sdk-reference/graphql-api/run-progress). Nothing is backfilled: every manifest from before, and
every one scheduled without it, records nobody.
