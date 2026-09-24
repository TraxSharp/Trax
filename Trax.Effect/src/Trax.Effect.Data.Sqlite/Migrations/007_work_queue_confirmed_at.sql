-- See the Postgres migration of the same name. SQLite cannot add a column with a non-constant
-- default, and a SQLite database has a single writer process, so there is no rolling deploy for
-- the Postgres default to cover. For the same reason no dispatch can be in flight while this
-- runs, so dispatched history (status 1) is simply left null.
ALTER TABLE work_queue ADD COLUMN confirmed_at TEXT NULL;

UPDATE work_queue SET confirmed_at = created_at WHERE confirmed_at IS NULL AND status <> 1;

-- Queued rows only (status 0), matching the sweep's predicate.
CREATE INDEX IF NOT EXISTS ix_work_queue_unconfirmed
    ON work_queue (created_at)
    WHERE confirmed_at IS NULL AND status = 0;
