-- Add 'once' value to schedule_type enum for one-off delayed jobs
ALTER TYPE trax.schedule_type ADD VALUE IF NOT EXISTS 'once';

-- Add scheduled_at column to manifest (used by Once schedule type)
ALTER TABLE trax.manifest
    ADD COLUMN IF NOT EXISTS scheduled_at timestamptz;

-- Add scheduled_at column to work_queue (used by delayed triggers)
ALTER TABLE trax.work_queue
    ADD COLUMN IF NOT EXISTS scheduled_at timestamptz;

-- Index for efficiently finding work queue entries with future scheduled_at.
-- Note: no partial index on manifest for schedule_type='once' here because
-- new enum values cannot be referenced in the same transaction they are added.
-- The existing ix_manifest_scheduling index covers enabled manifests.
CREATE INDEX IF NOT EXISTS ix_work_queue_scheduled_at
    ON trax.work_queue (scheduled_at)
    WHERE status = 'queued' AND scheduled_at IS NOT NULL;
