-- 001 and 004 wrote their partial-index predicates with the Postgres labels ('queued', 'pending',
-- 'failed', 'awaiting_intervention'), but SQLite stores an enum as its integer (see 008), so those
-- indexes covered no row EF writes. The unique one enforced nothing: a manifest could hold two
-- queued entries. This recreates each of them comparing integers:
--   WorkQueueStatus   Queued = 0
--   TrainState        Pending = 0, Completed = 1, Failed = 2, InProgress = 3, Cancelled = 4
--   DeadLetterStatus  AwaitingIntervention = 0
--
-- While the unique index was inert a manifest could collect more than one queued entry, and the
-- unique index cannot be built over them. Every queued entry for a manifest but its oldest is
-- cancelled, which is what the dispatcher would have been left with had the index held.
UPDATE work_queue
SET status = 2
WHERE status = 0
  AND manifest_id IS NOT NULL
  AND id <> (
      SELECT MIN(w.id) FROM work_queue w
      WHERE w.manifest_id = work_queue.manifest_id AND w.status = 0
  );

DROP INDEX IF EXISTS ix_work_queue_status;
CREATE INDEX IF NOT EXISTS ix_work_queue_status ON work_queue (status) WHERE status = 0;

DROP INDEX IF EXISTS ix_work_queue_status_priority;
CREATE INDEX IF NOT EXISTS ix_work_queue_status_priority
    ON work_queue (status, priority DESC, created_at ASC) WHERE status = 0;

DROP INDEX IF EXISTS ix_work_queue_unique_queued_manifest;
CREATE UNIQUE INDEX IF NOT EXISTS ix_work_queue_unique_queued_manifest
    ON work_queue (manifest_id) WHERE status = 0 AND manifest_id IS NOT NULL;

DROP INDEX IF EXISTS ix_work_queue_scheduled_at;
CREATE INDEX IF NOT EXISTS ix_work_queue_scheduled_at
    ON work_queue (scheduled_at) WHERE status = 0 AND scheduled_at IS NOT NULL;

DROP INDEX IF EXISTS ix_work_queue_manifest_id_status_queued;
CREATE INDEX IF NOT EXISTS ix_work_queue_manifest_id_status_queued
    ON work_queue (manifest_id, status) WHERE status = 0;

DROP INDEX IF EXISTS ix_metadata_train_state_start_time;
CREATE INDEX IF NOT EXISTS ix_metadata_train_state_start_time
    ON metadata (train_state, start_time) WHERE train_state IN (0, 3);

DROP INDEX IF EXISTS ix_metadata_manifest_id_train_state;
CREATE INDEX IF NOT EXISTS ix_metadata_manifest_id_train_state
    ON metadata (manifest_id, train_state) WHERE train_state IN (0, 3);

DROP INDEX IF EXISTS ix_metadata_active_capacity;
CREATE INDEX IF NOT EXISTS ix_metadata_active_capacity
    ON metadata (train_state, manifest_id) WHERE train_state IN (0, 3);

DROP INDEX IF EXISTS ix_metadata_cleanup;
CREATE INDEX IF NOT EXISTS ix_metadata_cleanup
    ON metadata (name, start_time) WHERE train_state IN (1, 2, 4);

DROP INDEX IF EXISTS ix_metadata_manifest_failed;
CREATE INDEX IF NOT EXISTS ix_metadata_manifest_failed
    ON metadata (manifest_id, start_time) WHERE train_state = 2;

DROP INDEX IF EXISTS dead_letter_status_idx;
CREATE INDEX IF NOT EXISTS dead_letter_status_idx ON dead_letter (status) WHERE status = 0;
