-- See the Postgres migration of the same name. WorkQueueStatus.Queued is stored as 0 (see 014).
UPDATE work_queue
SET replay_decisions_of = NULL
WHERE status = 0
  AND replay_decisions_of IS NOT NULL
  AND EXISTS (
      SELECT 1 FROM work_queue AS o
      WHERE o.status = 0
        AND o.replay_decisions_of = work_queue.replay_decisions_of
        AND o.id < work_queue.id
  );

CREATE UNIQUE INDEX IF NOT EXISTS ix_work_queue_unique_queued_replay
    ON work_queue (replay_decisions_of)
    WHERE status = 0 AND replay_decisions_of IS NOT NULL;
