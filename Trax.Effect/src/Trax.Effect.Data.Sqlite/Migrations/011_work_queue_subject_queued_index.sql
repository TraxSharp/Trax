-- See the Postgres migration of the same name. Queued is status 0.
CREATE INDEX IF NOT EXISTS ix_work_queue_subject_queued
    ON work_queue (subject_key)
    WHERE status = 0 AND subject_key IS NOT NULL;
