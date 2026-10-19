-- See the Postgres migration of the same name: finds a state-machine instance by its id alone.
CREATE INDEX IF NOT EXISTS ix_snapshot_draft_id
    ON snapshot_draft (id);
