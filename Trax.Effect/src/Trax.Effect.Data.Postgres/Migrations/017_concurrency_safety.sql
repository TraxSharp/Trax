-- Defense-in-depth: prevent duplicate queued work queue entries for the same manifest.
-- Application-level advisory locks prevent this; this index enforces it at the DB level.
CREATE UNIQUE INDEX IF NOT EXISTS ix_work_queue_unique_queued_manifest
    ON trax.work_queue (manifest_id)
    WHERE status = 'queued' AND manifest_id IS NOT NULL;
