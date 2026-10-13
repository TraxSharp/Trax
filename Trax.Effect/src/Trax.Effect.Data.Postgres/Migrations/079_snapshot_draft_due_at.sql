-- When a state-machine instance's timer fires (Trax.Effect.StateMachine.Persistence, Trax.Docs/adr/0048).
-- Mirrors SnapshotDraft.DueAt.
--
-- due_at: when the timer of the state the instance is in fires, or null when that state has no timer.
-- Every write that enters a timed state sets it, in the same statement as the state, from the
-- database's clock plus the state's duration; a write that leaves the state clears it, and a write
-- that stays in it keeps the time it was given at entry. Every host that registers the machine sweeps
-- the rows whose due_at has passed, on this clock, and fires each one by a conditional update on its
-- state and revision. A row a later definition gives no timer is cleared by that sweep. Server-only:
-- it travels beside the snapshot, never inside it. Existing rows read null.
--
-- ix_snapshot_draft_due: the sweep's query, the due rows oldest first. Partial, so it holds only the
-- rows that wait on a timer.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS due_at timestamptz NULL;

-- CONCURRENTLY so drafts keep being saved and advanced while it builds (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_snapshot_draft_due
    ON trax.snapshot_draft (due_at, row_id)
    WHERE due_at IS NOT NULL;
