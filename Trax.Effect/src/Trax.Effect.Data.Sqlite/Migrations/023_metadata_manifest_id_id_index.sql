-- See the Postgres migration of the same name.
CREATE INDEX IF NOT EXISTS ix_metadata_manifest_id_id
    ON metadata (manifest_id, id DESC) WHERE manifest_id IS NOT NULL;
