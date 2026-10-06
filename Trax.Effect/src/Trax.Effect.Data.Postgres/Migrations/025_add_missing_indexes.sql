-- Add missing indexes for manifest pruning and metadata queries.
-- Without these, DELETE/SELECT queries on large tables do full sequential scans,
-- causing timeouts during scheduler startup (PruneStaleManifests, ScheduleMany).

-- manifest.external_id: used by prune prefix queries (StartsWith), lookups, and upserts
CREATE UNIQUE INDEX IF NOT EXISTS ix_manifest_external_id
    ON trax.manifest (external_id);

-- metadata.manifest_id: used by cascade deletes during manifest pruning
CREATE INDEX IF NOT EXISTS ix_metadata_manifest_id
    ON trax.metadata (manifest_id);

-- metadata.(name, train_state): used by stuck job recovery and cleanup queries
CREATE INDEX IF NOT EXISTS ix_metadata_name_train_state
    ON trax.metadata (name, train_state);

-- work_queue.manifest_id: used by cascade deletes during manifest pruning
CREATE INDEX IF NOT EXISTS ix_work_queue_manifest_id
    ON trax.work_queue (manifest_id);

-- dead_letter.manifest_id: used by cascade deletes during manifest pruning
CREATE INDEX IF NOT EXISTS ix_dead_letter_manifest_id
    ON trax.dead_letter (manifest_id);
