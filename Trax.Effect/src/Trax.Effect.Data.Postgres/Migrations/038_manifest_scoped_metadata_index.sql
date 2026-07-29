-- Covering index for the manifest- and group-scoped operations reads behind the dashboard's
-- manifest detail and group detail pages: operations.executions filtered by manifestId /
-- manifestGroupId (a manifest's or group's execution history), and the per-manifest /
-- per-group execution stat aggregations (counts GROUP BY train_state, plus last run time).
--
-- Without it, filtering the multi-million-row metadata table by manifest_id degrades to a full
-- sequential scan, and the stat aggregation heap-fetches every matching row. The existing
-- ix_metadata_manifest_failed only covers failed rows, so it cannot serve all-state history or
-- the completed/in-progress/cancelled counts.
--
-- (manifest_id, train_state) INCLUDE (start_time, end_time): Postgres seeks the manifest
-- partition, and the per-state counts plus max(start_time)/max(end_time) read straight from the
-- index (Index Only Scan once autovacuum has set the visibility map). The manifest side of the
-- group filter is already served by ix_manifest_manifest_group_id (migration 014). Partial
-- because scheduled runs always carry a manifest_id; ad-hoc queued trains (manifest_id NULL) are
-- never manifest-scoped and would only bloat the index.
CREATE INDEX IF NOT EXISTS ix_metadata_manifest_state
    ON trax.metadata (manifest_id, train_state)
    INCLUDE (start_time, end_time)
    WHERE manifest_id IS NOT NULL;
