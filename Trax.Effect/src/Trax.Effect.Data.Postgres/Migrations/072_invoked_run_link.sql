-- Links a run that a state machine's invoking state queued back to the instance that queued it
-- (Trax.Effect.StateMachine.Persistence). Columns mirror WorkQueue and Metadata exactly.
--
-- invoking_machine, invoking_instance_id, invoking_owner_kind: the machine, the draft or system
-- instance id, and whether a user or the system owns it. The enqueue writes them on the work queue
-- entry and the dispatcher copies them to the run's metadata, so the link outlives the instance's
-- invoke_token, which is cleared once the state is left. They are null on every other run. The
-- operations service refuses to requeue a run that carries them, and the operator view of an
-- instance lists its runs through ix_metadata_invoking_instance.
--
-- The owner kind reuses trax.snapshot_owner_kind (070), so the vocabulary is one enum.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.work_queue
    ADD COLUMN IF NOT EXISTS invoking_machine text NULL;

ALTER TABLE trax.work_queue
    ADD COLUMN IF NOT EXISTS invoking_instance_id uuid NULL;

ALTER TABLE trax.work_queue
    ADD COLUMN IF NOT EXISTS invoking_owner_kind trax.snapshot_owner_kind NULL;

ALTER TABLE trax.metadata
    ADD COLUMN IF NOT EXISTS invoking_machine text NULL;

ALTER TABLE trax.metadata
    ADD COLUMN IF NOT EXISTS invoking_instance_id uuid NULL;

ALTER TABLE trax.metadata
    ADD COLUMN IF NOT EXISTS invoking_owner_kind trax.snapshot_owner_kind NULL;

-- CONCURRENTLY so runs carry on being written while it builds (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_metadata_invoking_instance
    ON trax.metadata (invoking_machine, invoking_instance_id)
    WHERE invoking_instance_id IS NOT NULL;
