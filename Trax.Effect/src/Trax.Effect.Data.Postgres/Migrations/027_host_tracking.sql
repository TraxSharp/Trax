ALTER TABLE trax.metadata ADD COLUMN host_name varchar;
ALTER TABLE trax.metadata ADD COLUMN host_environment varchar;
ALTER TABLE trax.metadata ADD COLUMN host_instance_id varchar;
ALTER TABLE trax.metadata ADD COLUMN host_labels jsonb;

CREATE INDEX IF NOT EXISTS ix_metadata_host_name ON trax.metadata (host_name);
CREATE INDEX IF NOT EXISTS ix_metadata_host_environment ON trax.metadata (host_environment);
CREATE INDEX IF NOT EXISTS ix_metadata_host_labels ON trax.metadata USING gin (host_labels) WHERE host_labels IS NOT NULL;
