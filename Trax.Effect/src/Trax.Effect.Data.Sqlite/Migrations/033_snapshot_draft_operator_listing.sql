-- See the Postgres migration of the same name. SQLite has no INCLUDE columns, so the listing's
-- index by machine and state holds the key columns only.
ALTER TABLE snapshot_draft ADD COLUMN created_at TEXT NULL;

DROP INDEX IF EXISTS ix_snapshot_draft_machine_state;

CREATE INDEX IF NOT EXISTS ix_snapshot_draft_machine_state_updated
    ON snapshot_draft (machine, state, updated_at DESC, row_id DESC);

CREATE INDEX IF NOT EXISTS ix_snapshot_draft_updated
    ON snapshot_draft (updated_at DESC, row_id DESC);
