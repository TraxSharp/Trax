-- Who a manifest's runs are started for (Trax.Docs/adr/0051), so the caller who scheduled a one-off manifest can follow
-- each of its runs and retries through runProgress.
--
-- starter_hash: the lowercase hex SHA-256 of "p\0" and the starter's qualified principal id, as on work_queue and
-- metadata (082).
-- Set only when the manifest is scheduled with ScheduleOptions.StartedBy; the scheduler copies it onto every work queue
-- entry it queues for the manifest, which the dispatcher copies to the run's metadata. Every manifest from before this
-- migration, and every one scheduled without it, has none.
--
-- The statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.manifest
    ADD COLUMN IF NOT EXISTS starter_hash text NULL;
