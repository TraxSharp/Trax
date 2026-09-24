-- Filtering runs by how they failed ("the conflicts this hour") pages by id, like every other
-- executions filter. Without an index that is a scan of the whole metadata table and an exact
-- count over it. Not partial: the class arrives as a query parameter, and a generic plan cannot
-- prove a parameter satisfies a partial index's predicate, so a partial index would go unused.
CREATE INDEX IF NOT EXISTS ix_metadata_failure_class
    ON trax.metadata (failure_class, id DESC);
