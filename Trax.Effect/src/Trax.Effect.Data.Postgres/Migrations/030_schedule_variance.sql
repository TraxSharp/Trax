ALTER TABLE trax.manifest ADD COLUMN IF NOT EXISTS variance_seconds integer;
ALTER TABLE trax.manifest ADD COLUMN IF NOT EXISTS next_scheduled_run timestamp without time zone;
