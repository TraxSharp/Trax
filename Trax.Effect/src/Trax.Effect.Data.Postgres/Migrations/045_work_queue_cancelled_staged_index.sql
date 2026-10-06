-- The stale-staged sweep deletes staged entries it cancelled once they are past their retention
-- (effect/0007). It runs on every ManifestManager cycle, and work_queue keeps dispatched rows
-- until metadata cleanup removes them, so without an index that delete is a scan of the whole
-- table on every cycle. The rows it looks for are few, one per enqueue a crash stranded, so a
-- partial index over just those costs next to nothing to keep.
--
-- ix_work_queue_unconfirmed cannot serve it: it covers queued rows, and a cancelled row leaves it
-- by design. Both status and confirmed_at are literals in the delete, so the planner can prove
-- the predicate; created_at is the only parameter.
CREATE INDEX IF NOT EXISTS ix_work_queue_cancelled_staged
    ON trax.work_queue (created_at)
    WHERE confirmed_at IS NULL AND status = 'cancelled';
