-- Add priority column to manifest (default priority for workflows scheduled via this manifest)
ALTER TABLE trax.manifest
    ADD COLUMN priority smallint NOT NULL DEFAULT 0;

-- Add priority column to work_queue (effective priority at dispatch time)
ALTER TABLE trax.work_queue
    ADD COLUMN priority smallint NOT NULL DEFAULT 0;

-- Replace the existing partial index with a composite index that supports
-- ORDER BY priority DESC, created_at ASC for efficient dispatcher queries.
DROP INDEX IF EXISTS trax.ix_work_queue_status;
CREATE INDEX ix_work_queue_status_priority
    ON trax.work_queue (status, priority DESC, created_at ASC)
    WHERE status = 'queued';
