-- A manifest's runs newest first, by id. Junction events read it once as each run of a manifest
-- begins, to tell which attempt the run is, so the read takes the manifest's latest runs from the
-- index instead of sorting all of them. Built CONCURRENTLY so it does not block writes to
-- metadata (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_metadata_manifest_id_id
    ON trax.metadata (manifest_id, id DESC) WHERE manifest_id IS NOT NULL;
