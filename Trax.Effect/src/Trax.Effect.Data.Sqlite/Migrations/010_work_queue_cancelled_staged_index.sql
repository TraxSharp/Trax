-- See the Postgres migration of the same name. Cancelled is status 2.
CREATE INDEX IF NOT EXISTS ix_work_queue_cancelled_staged
    ON work_queue (created_at)
    WHERE confirmed_at IS NULL AND status = 2;
