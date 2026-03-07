-- Indexes for scaling bottlenecks identified by database audit.
-- These cover query patterns that become sequential scans at high row counts.

-- metadata(train_state, start_time): used by ReapStalePendingMetadataStep and
-- SchedulerStartupService to find stale/stuck jobs. Partial index on active states
-- keeps the index small while covering the hot path.
CREATE INDEX IF NOT EXISTS ix_metadata_train_state_start_time
    ON trax.metadata (train_state, start_time)
    WHERE train_state IN ('pending', 'in_progress');

-- metadata(start_time DESC): used by Dashboard KPI aggregations (daily counts,
-- hourly time-series, top failures) and API GetExecutions pagination.
-- Without this, GROUP BY start_time queries do full sequential scans.
CREATE INDEX IF NOT EXISTS ix_metadata_start_time_desc
    ON trax.metadata (start_time DESC);

-- metadata(manifest_id, train_state): used by LoadDispatchCapacityStep to count
-- active jobs per manifest group, and by CancelTimedOutJobsStep to find timed-out
-- in-progress jobs. The existing ix_metadata_manifest_id doesn't cover train_state
-- so the DB has to filter post-lookup. Partial index keeps it lean.
CREATE INDEX IF NOT EXISTS ix_metadata_manifest_id_train_state
    ON trax.metadata (manifest_id, train_state)
    WHERE train_state IN ('pending', 'in_progress');

-- metadata(end_time DESC): used by TraxHealthService to count recent failures
-- (WHERE train_state='failed' AND end_time > cutoff). Without this, the health
-- check does a full scan on every poll.
CREATE INDEX IF NOT EXISTS ix_metadata_end_time_desc
    ON trax.metadata (end_time DESC)
    WHERE end_time IS NOT NULL;

-- background_job(created_at) WHERE fetched_at IS NULL: used by LocalWorkerService
-- FOR UPDATE SKIP LOCKED dequeue query. The table should stay small, but without
-- any index, N concurrent workers each do a full table scan per poll cycle.
CREATE INDEX IF NOT EXISTS ix_background_job_unfetched
    ON trax.background_job (created_at ASC)
    WHERE fetched_at IS NULL;

-- work_queue(manifest_id, status) WHERE status='queued': used by
-- DormantDependentContext.ActivateSingleAsync to check for existing queued work
-- before activating a dormant dependent. Without this composite, the single-column
-- manifest_id index requires a post-filter on status.
CREATE INDEX IF NOT EXISTS ix_work_queue_manifest_id_status_queued
    ON trax.work_queue (manifest_id, status)
    WHERE status = 'queued';
