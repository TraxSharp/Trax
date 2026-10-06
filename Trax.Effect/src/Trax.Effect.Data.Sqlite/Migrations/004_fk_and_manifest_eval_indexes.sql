-- Foreign-key back-reference indexes and a manifest failed-count index.
-- Mirrors Postgres migration 036. SQLite enforces the same FK RESTRICT checks, so the
-- referencing columns need indexes to keep DeleteExpiredMetadataJunction's DELETE off a
-- full scan, and LoadManifestsJunction's per-manifest FailedCount needs a partial index to
-- stay bounded to failed rows as terminal history accumulates.

CREATE INDEX IF NOT EXISTS ix_metadata_parent_id
    ON metadata (parent_id)
    WHERE parent_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_work_queue_metadata_id
    ON work_queue (metadata_id)
    WHERE metadata_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_dead_letter_retry_metadata_id
    ON dead_letter (retry_metadata_id)
    WHERE retry_metadata_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_metadata_manifest_failed
    ON metadata (manifest_id, start_time)
    WHERE train_state = 'failed';
