-- Covering indexes for the dashboard metrics block (operations.metrics.dashboard /
-- IOperationsService.GetDashboardMetricsAsync). That block runs several aggregations
-- over the last-7-day and last-24h windows on every dashboard load. At millions of
-- rows they degraded into bitmap-heap scans of the whole window: top failures, top
-- average durations, and the throughput series each scanned 300k-1.5M metadata rows
-- and heap-fetched every one. The Trax.Api stress suite measured the block at
-- ~630-770ms at 3M metadata rows, over the 750ms dashboard budget (the hideAdminTrains
-- variant broke it outright).
--
-- These two INCLUDE-covering indexes turn every metrics aggregation into a heap-free
-- Index Only Scan (Heap Fetches: 0 once autovacuum has set the visibility map), which
-- dropped the block to ~350-470ms at the same 3M rows with room to grow.

-- (train_state, start_time) INCLUDE (name, end_time, parent_id): serves the single-
-- state, time-windowed aggregations that GROUP BY name -- top failures (state=failed),
-- top average durations (state=completed, parent_id IS NULL, using end_time), and the
-- 7-day throughput series (state=completed). Postgres seeks the state partition,
-- range-scans start_time, and reads name/end_time/parent_id straight from the index.
CREATE INDEX IF NOT EXISTS ix_metadata_metrics_state_time
    ON trax.metadata (train_state, start_time)
    INCLUDE (name, end_time, parent_id);

-- (start_time) INCLUDE (train_state, name): serves the all-state window aggregations --
-- today's KPI counts (GROUP BY train_state) and the executions-over-time series
-- (GROUP BY hour/minute, train_state). The included name column lets the admin-train
-- exclusion (WHERE name <> ALL(...)) stay index-only too.
CREATE INDEX IF NOT EXISTS ix_metadata_metrics_window
    ON trax.metadata (start_time)
    INCLUDE (train_state, name);
