-- See the Postgres migration of the same name. SQLite stores enums as their integers, so the
-- predicates compare integers: WorkQueueStatus.Dispatched = 1, TrainState.Pending = 0 and
-- TrainState.InProgress = 3.
ALTER TABLE work_queue ADD COLUMN subject_key TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_work_queue_subject_busy
    ON work_queue (subject_key)
    WHERE status = 1;

CREATE INDEX IF NOT EXISTS ix_metadata_active
    ON metadata (id)
    WHERE train_state IN (0, 3);
