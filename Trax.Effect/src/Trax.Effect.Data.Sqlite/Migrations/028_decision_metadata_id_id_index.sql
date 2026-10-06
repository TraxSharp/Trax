-- See the Postgres migration of the same name.
CREATE INDEX IF NOT EXISTS ix_decision_metadata_id_id
    ON decision (metadata_id, id);
