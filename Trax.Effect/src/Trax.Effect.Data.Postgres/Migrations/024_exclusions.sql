-- Add exclusions JSONB column to manifest for calendar/exclusion window support
ALTER TABLE trax.manifest
    ADD COLUMN IF NOT EXISTS exclusions jsonb;
