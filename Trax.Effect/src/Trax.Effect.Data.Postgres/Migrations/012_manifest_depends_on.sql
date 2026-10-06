-- Add 'dependent' value to schedule_type enum for manifests that trigger after a parent succeeds
ALTER TYPE trax.schedule_type ADD VALUE IF NOT EXISTS 'dependent';

-- Add self-referencing FK column for dependent manifest relationships
ALTER TABLE trax.manifest
    ADD COLUMN depends_on_manifest_id int REFERENCES trax.manifest(id) ON DELETE SET NULL;

-- Partial index for efficient lookup of dependent manifests
CREATE INDEX ix_manifest_depends_on
    ON trax.manifest (depends_on_manifest_id) WHERE depends_on_manifest_id IS NOT NULL;
