-- Singleton-row table holding persisted scheduler runtime settings.
-- The CHECK constraint enforces "exactly one row" by limiting id to 1.
CREATE TABLE IF NOT EXISTS trax.scheduler_config (
    id bigint PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    manifest_manager_enabled boolean NOT NULL DEFAULT true,
    job_dispatcher_enabled boolean NOT NULL DEFAULT true,
    manifest_manager_polling_interval interval NOT NULL DEFAULT '5 seconds',
    job_dispatcher_polling_interval interval NOT NULL DEFAULT '2 seconds',
    max_active_jobs integer,
    default_max_retries integer NOT NULL DEFAULT 3,
    default_retry_delay interval NOT NULL DEFAULT '5 minutes',
    retry_backoff_multiplier double precision NOT NULL DEFAULT 2.0,
    max_retry_delay interval NOT NULL DEFAULT '1 hour',
    default_job_timeout interval NOT NULL DEFAULT '20 minutes',
    stale_pending_timeout interval NOT NULL DEFAULT '20 minutes',
    recover_stuck_jobs_on_startup boolean NOT NULL DEFAULT true,
    dead_letter_retention_period interval NOT NULL DEFAULT '30 days',
    auto_purge_dead_letters boolean NOT NULL DEFAULT true,
    local_worker_count integer,
    metadata_cleanup_interval interval,
    metadata_cleanup_retention interval,
    updated_at timestamptz NOT NULL DEFAULT now()
);
