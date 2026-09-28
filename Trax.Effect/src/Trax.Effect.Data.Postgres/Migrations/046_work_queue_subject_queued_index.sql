-- A queued entry's detail says which queued sibling for the same subject dispatch would take first,
-- and the dashboard's detail page asks the same question. Neither index from 042 serves it:
-- ix_work_queue_subject_busy covers dispatched rows only, so without this the lookup walks every
-- queued row in the table filtering on subject_key (measured at 36ms with 51k queued rows, growing
-- with the backlog).
--
-- Only entries enqueued with a subject carry a key, and most of a backlog is manifest work that
-- carries none, so the index leaves the nulls out. The planner proves subject_key IS NOT NULL
-- from the lookup's subject_key = $1, so the predicate does not stop it being used.
CREATE INDEX IF NOT EXISTS ix_work_queue_subject_queued
    ON trax.work_queue (subject_key)
    WHERE status = 'queued' AND subject_key IS NOT NULL;
