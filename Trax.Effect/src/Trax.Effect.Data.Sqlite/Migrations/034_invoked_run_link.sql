-- See the Postgres migration of the same name: the link from a run a state machine's invoking state
-- queued back to the instance that queued it, on the work queue entry and on the run's metadata.
--
-- invoking_owner_kind is the integer of SnapshotOwnerKind: 0 is a user, 1 is the system.
ALTER TABLE work_queue ADD COLUMN invoking_machine TEXT NULL;

ALTER TABLE work_queue ADD COLUMN invoking_instance_id TEXT NULL;

ALTER TABLE work_queue ADD COLUMN invoking_owner_kind INTEGER NULL;

ALTER TABLE metadata ADD COLUMN invoking_machine TEXT NULL;

ALTER TABLE metadata ADD COLUMN invoking_instance_id TEXT NULL;

ALTER TABLE metadata ADD COLUMN invoking_owner_kind INTEGER NULL;

CREATE INDEX IF NOT EXISTS ix_metadata_invoking_instance
    ON metadata (invoking_machine, invoking_instance_id)
    WHERE invoking_instance_id IS NOT NULL;
