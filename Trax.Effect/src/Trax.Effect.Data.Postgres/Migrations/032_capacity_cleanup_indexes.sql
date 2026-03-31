-- Indexes for LoadDispatchCapacityJunction and DeleteExpiredMetadataJunction query patterns.
-- These reduce lock contention and improve query efficiency on shared database instances.

-- Covering partial index for LoadDispatchCapacityJunction.
-- The capacity query filters on train_state IN ('pending', 'in_progress'), excludes
-- rows by name, and joins on manifest_id. INCLUDEing name allows the exclusion
-- filter to be evaluated from the index without heap access.
CREATE INDEX IF NOT EXISTS ix_metadata_active_capacity
    ON trax.metadata (train_state, manifest_id)
    INCLUDE (name)
    WHERE train_state IN ('pending', 'in_progress');

-- Partial index for DeleteExpiredMetadataJunction cleanup queries.
-- The cleanup query filters WHERE name = ANY(@whitelist) AND start_time < @cutoff
-- AND train_state IN ('completed', 'failed', 'cancelled'). This index covers
-- terminal-state rows for efficient name + start_time filtering.
CREATE INDEX IF NOT EXISTS ix_metadata_cleanup
    ON trax.metadata (name, start_time)
    WHERE train_state IN ('completed', 'failed', 'cancelled');
