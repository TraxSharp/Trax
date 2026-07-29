-- Covering index for the cluster (hosts) rollup (operations.hosts /
-- OperationsQueries.GetHosts). That endpoint groups the whole metadata table by host instance to
-- answer "which processes have run work, and are they still alive". It has no time filter, so it
-- aggregates every row: an inherently O(rows) read that no index can turn sub-second at millions
-- of rows (the Trax.Api stress suite measures it around 800ms at 3M metadata rows). It is a
-- refresh-on-demand admin view, not a hot path, so that latency is acceptable and it runs under
-- the dedicated cluster budget.
--
-- This index still earns its keep: ordering by the GROUP BY key lets Postgres stream the aggregate
-- (no hash table), and the INCLUDE columns make it a heap-free Index Only Scan, so the rollup does
-- not fetch 3M heap tuples and holds up better under cold cache and concurrent load than the warm
-- single-query benchmark alone shows. The partial predicate matches the query's
-- WHERE host_instance_id IS NOT NULL, so the index only carries rows the rollup reads.
CREATE INDEX IF NOT EXISTS ix_metadata_host_rollup
    ON trax.metadata (host_instance_id, host_name, host_environment)
    INCLUDE (start_time, train_state)
    WHERE host_instance_id IS NOT NULL;
