-- Add priority column to background_job for priority-aware dequeue.
-- Workers now ORDER BY priority DESC, created_at ASC instead of created_at ASC alone.
-- Default 0 preserves existing behavior for rows without explicit priority.
ALTER TABLE trax.background_job
    ADD COLUMN IF NOT EXISTS priority integer NOT NULL DEFAULT 0;

-- Replace dequeue indexes with priority-aware versions.
DROP INDEX IF EXISTS trax.ix_background_job_dequeue;
DROP INDEX IF EXISTS trax.ix_background_job_unfetched;

CREATE INDEX ix_background_job_unfetched
    ON trax.background_job (priority DESC, created_at ASC)
    WHERE fetched_at IS NULL;
