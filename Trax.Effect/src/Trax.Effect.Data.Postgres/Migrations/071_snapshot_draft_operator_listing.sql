-- What the operator listing of state-machine instances reads (Trax.Scheduler's OperationsService,
-- behind operations.machineInstances and the dashboard's State machines page). Columns mirror
-- SnapshotDraft exactly.
--
-- created_at: when the row was created, written by the store on insert and never changed after.
-- Nullable, because nothing recorded when an existing row was created, and copying updated_at
-- into it would state a creation time that is only the last write. A host still running the
-- previous version inserts without it, and its rows read as "not recorded" too.
--
-- The listing is newest first by updated_at, filtered by machine and state, both optional. 070's
-- (machine, state) index finds a state's rows but not in that order, so a page of a state
-- holding a million rows sorted all of them. It is replaced by one that holds each state's rows
-- in the listing's order, with owner_kind included so the counts by machine, state and owner
-- kind read the index alone. A listing with no state filter reads the second index in order
-- and stops at its page; a machine filter there is checked row by row, which is cheap while a
-- machine's rows are a fair share of the table, and the planner reads the first index for a
-- machine whose rows are few. The indexes are built and the old one dropped CONCURRENTLY, so
-- drafts keep being saved and advanced meanwhile (effect/0014).
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS created_at timestamptz NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_snapshot_draft_machine_state_updated
    ON trax.snapshot_draft (machine, state, updated_at DESC, row_id DESC)
    INCLUDE (owner_kind);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_snapshot_draft_updated
    ON trax.snapshot_draft (updated_at DESC, row_id DESC);

DROP INDEX CONCURRENTLY IF EXISTS trax.ix_snapshot_draft_machine_state;
