-- Foreign-key back-reference indexes and a manifest failed-count index.
-- These stop two hot paths from degrading to O(table) as metadata history accumulates.

-- PostgreSQL does not auto-index the referencing side of a foreign key. Deleting a metadata
-- row therefore forced DeleteExpiredMetadataJunction to sequentially scan every table that
-- references metadata(id) to satisfy the ON DELETE RESTRICT / NO ACTION checks. On a bloated
-- table each cleanup delete became O(table). Partial (NOT NULL) keeps these lean: the vast
-- majority of rows have no parent, no metadata link, and no retry.
CREATE INDEX IF NOT EXISTS ix_metadata_parent_id
    ON trax.metadata (parent_id)
    WHERE parent_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_work_queue_metadata_id
    ON trax.work_queue (metadata_id)
    WHERE metadata_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_dead_letter_retry_metadata_id
    ON trax.dead_letter (retry_metadata_id)
    WHERE retry_metadata_id IS NOT NULL;

-- LoadManifestsJunction computes a FailedCount per manifest on every dispatch cycle:
-- COUNT(metadata WHERE manifest_id = ? AND train_state = 'failed'), minus rows already
-- resolved by a dead letter. The existing partial indexes only cover pending/in_progress,
-- so this degraded to a per-manifest scan of all terminal history. This partial index
-- bounds it to the (normally rare) failed rows. start_time is in the key because the
-- dead-letter anti-join filters on metadata.start_time.
CREATE INDEX IF NOT EXISTS ix_metadata_manifest_failed
    ON trax.metadata (manifest_id, start_time)
    WHERE train_state = 'failed';
