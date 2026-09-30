-- See the Postgres migration of the same name. SQLite stores jsonb as TEXT, a boolean as an
-- integer, and the work queue status as its enum's integer (WorkQueueStatus.Queued = 0).
ALTER TABLE scheduler_config ADD COLUMN overrides TEXT;

ALTER TABLE manifest ADD COLUMN failure_window_seconds INTEGER
    CHECK (failure_window_seconds IS NULL OR failure_window_seconds > 0);

ALTER TABLE manifest ADD COLUMN owner TEXT;

ALTER TABLE work_queue ADD COLUMN is_explicit_trigger INTEGER NOT NULL DEFAULT 0;

UPDATE work_queue
SET is_explicit_trigger = 1
WHERE dead_letter_id IS NOT NULL
  AND status = 0
  AND is_explicit_trigger = 0;
