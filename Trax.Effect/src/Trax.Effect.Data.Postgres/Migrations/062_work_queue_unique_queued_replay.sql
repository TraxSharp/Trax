-- A run's recorded answers are replayed by at most one queued entry at a time: a requeue, a dead
-- letter's requeue and a manifest's retry that land in the same instant cannot both queue a replay of
-- the same run. The application checks first; this index holds where two checks race.
--
-- An entry already queued beside an older one replaying the same run keeps its work and asks afresh:
-- its link is cleared, as the scheduler clears the link of a retry whose source is already being
-- replayed. Built CONCURRENTLY so enqueue and dispatch keep writing while it builds (effect/0014).
UPDATE trax.work_queue AS w
SET replay_decisions_of = NULL
WHERE w.status = 'queued'
  AND w.replay_decisions_of IS NOT NULL
  AND EXISTS (
      SELECT 1 FROM trax.work_queue AS o
      WHERE o.status = 'queued'
        AND o.replay_decisions_of = w.replay_decisions_of
        AND o.id < w.id
  );

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ix_work_queue_unique_queued_replay
    ON trax.work_queue (replay_decisions_of)
    WHERE status = 'queued' AND replay_decisions_of IS NOT NULL;
