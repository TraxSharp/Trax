-- Sqlite parity for the Postgres metrics covering indexes (see the Postgres set,
-- 037_metrics_covering_indexes.sql). Sqlite has no INCLUDE clause, so the covered
-- columns go into the index key instead. Sqlite is not a millions-of-rows target,
-- but keeping the dashboard metrics query plans coherent across providers avoids
-- surprises when the same GetDashboardMetricsAsync LINQ runs on either backend.

-- Single-state, time-windowed aggregations that GROUP BY name (top failures, top
-- average durations, throughput series).
CREATE INDEX IF NOT EXISTS ix_metadata_metrics_state_time
    ON metadata (train_state, start_time, name, end_time, parent_id);

-- All-state window aggregations (today's KPI counts, executions-over-time series).
CREATE INDEX IF NOT EXISTS ix_metadata_metrics_window
    ON metadata (start_time, train_state, name);
