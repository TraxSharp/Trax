-- A consumer correlating its own records with runs looks them up by external_id, the one key that
-- is the same from enqueue to run. 001 indexed it as unique and 005 dropped that index when a
-- dispatch retry started adding runs under the same id, and nothing replaced it, so every such
-- lookup read the whole of metadata: 48-77 ms per lookup at a million rows, 0.05 ms with this.
--
-- Plain, not unique: one external_id can name several runs. external_id is char(32), so a caller
-- comparing a char(32) uses it without a cast.
--
-- Built CONCURRENTLY, because metadata is the largest and busiest table and a plain build blocks
-- every insert and update on it until it finishes (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_metadata_external_id
    ON trax.metadata (external_id);
