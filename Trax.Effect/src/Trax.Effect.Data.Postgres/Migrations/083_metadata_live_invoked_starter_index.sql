-- The live invoked runs of one user, which the invoked-run cap counts on every invoking write: the metadata rows a
-- user-owned instance queued that are still pending or in progress, by starter_hash (082). Partial, so it holds only
-- those rows.
--
-- Built CONCURRENTLY, because metadata is the largest and busiest table and a plain build blocks every insert and
-- update on it until it finishes (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_metadata_live_invoked_starter
    ON trax.metadata (starter_hash)
    WHERE starter_hash IS NOT NULL
      AND invoking_owner_kind = 'user'
      AND train_state IN ('pending', 'in_progress');
