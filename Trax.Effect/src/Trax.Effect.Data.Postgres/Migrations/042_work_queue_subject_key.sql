-- Subject key: entries naming the same subject are not dispatched concurrently.
--
-- Null means no serialization, which is every entry that existed before this column, so nothing
-- changes for work already queued.
ALTER TABLE trax.work_queue ADD COLUMN IF NOT EXISTS subject_key text NULL;

-- The claim asks "is another entry for this subject still running?" once per candidate per cycle.
-- Measured on 200k entries, these two partial indexes take that from 36ms to 0.05ms; without them
-- the query is two sequential scans and the dispatcher does not keep up.
CREATE INDEX IF NOT EXISTS ix_work_queue_subject_busy
    ON trax.work_queue (subject_key)
    WHERE status = 'dispatched';

CREATE INDEX IF NOT EXISTS ix_metadata_active
    ON trax.metadata (id)
    WHERE train_state IN ('pending', 'in_progress');
