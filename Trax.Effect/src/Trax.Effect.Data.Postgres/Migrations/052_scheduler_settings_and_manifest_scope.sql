-- Four additions the scheduler reads, in one migration. Every one is a new nullable column or one
-- with a default, so a host still running the previous version reads and writes these tables
-- unchanged, and a rolling deploy is safe.
--
-- scheduler_config.overrides: which settings a save named, as a JSON object of setting name to
-- value. Null on the existing row, which means what every save meant until now: every column holds
-- a chosen value.
ALTER TABLE trax.scheduler_config ADD COLUMN IF NOT EXISTS overrides jsonb;

-- manifest.failure_window_seconds: how far back a failed run counts toward the manifest's retries.
-- Null uses the scheduler's window. Zero or less would count no failure, and the manifest would never
-- be dead-lettered, so it is refused here as well as in Manifest.Create.
ALTER TABLE trax.manifest ADD COLUMN IF NOT EXISTS failure_window_seconds integer;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_manifest_failure_window_seconds_positive'
          AND conrelid = 'trax.manifest'::regclass
    ) THEN
        ALTER TABLE trax.manifest
            ADD CONSTRAINT ck_manifest_failure_window_seconds_positive
            CHECK (failure_window_seconds IS NULL OR failure_window_seconds > 0);
    END IF;
END $$;

-- manifest.owner: the application that declared the manifest, so a prune of undeclared manifests
-- keeps to its own. Existing rows have no owner.
ALTER TABLE trax.manifest ADD COLUMN IF NOT EXISTS owner text;

-- work_queue.is_explicit_trigger: the run was asked for by name, so it is dispatched while its
-- manifest is disabled. Until now only a dead-letter requeue was exempt from a disabled manifest,
-- and the scheduler told it apart by its dead letter, so a queued requeue is marked here and keeps
-- running exactly as it would have.
ALTER TABLE trax.work_queue
    ADD COLUMN IF NOT EXISTS is_explicit_trigger boolean NOT NULL DEFAULT false;

UPDATE trax.work_queue
SET is_explicit_trigger = true
WHERE dead_letter_id IS NOT NULL
  AND status = 'queued'
  AND NOT is_explicit_trigger;
