-- See the Postgres migration of the same name: due_at, when the timer of the state an instance is in
-- fires, or null when that state has no timer, and the partial index the due-timer sweep reads.
-- Stored as fixed-width UTC text (yyyy-MM-dd HH:mm:ss.fffffff+00:00), as every DateTimeOffset column
-- on SQLite, so text order is time order.
ALTER TABLE snapshot_draft ADD COLUMN due_at TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_snapshot_draft_due
    ON snapshot_draft (due_at, row_id)
    WHERE due_at IS NOT NULL;
