-- See the Postgres migration of the same name: the invoke token of the parent whose spawning state started this
-- instance, and the partial index that finds a parent's child by it.
ALTER TABLE snapshot_draft ADD COLUMN spawned_by TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_snapshot_draft_spawned_by
    ON snapshot_draft (spawned_by)
    WHERE spawned_by IS NOT NULL;
