-- Wakes the job dispatcher on every host when work becomes dispatchable, instead of leaving it to
-- the next poll. API hosts, where work is usually queued, are rarely the hosts that dispatch it.
--
-- pg_notify inside the writing transaction is delivered when that transaction commits and never
-- when it rolls back, and Postgres folds identical notifications sent in one transaction into
-- one. A trigger rather than a call at each enqueue site covers every writer, a caller's own
-- DbContext included. The payload is empty: a dispatcher that wakes loads what is ready itself.
--
-- An entry becomes dispatchable two ways, and each has a trigger:
--   * it is inserted. Once per statement, so a bulk insert by the manifest manager sends one. A
--     staged entry (confirmed_at null) sends one too, which only costs a cycle that finds nothing.
--   * a staged entry is confirmed (IWorkQueuePromotion).
-- A dispatch that fails and is put back to queued is not one of them: it carries a backoff in
-- scheduled_at, and waking for it would only run a cycle the backoff then skips. Nor are an entry
-- whose scheduled_at comes due or whose manifest is re-enabled; the poll covers those.
--
-- PostgresQueuedWorkListener in Trax.Effect.Data.Postgres listens on this channel by name.
CREATE OR REPLACE FUNCTION trax.notify_queued_work() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    PERFORM pg_notify('trax_queued_work', '');
    RETURN NULL;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'work_queue_inserted_notify'
          AND tgrelid = 'trax.work_queue'::regclass
    ) THEN
        CREATE TRIGGER work_queue_inserted_notify
            AFTER INSERT ON trax.work_queue
            FOR EACH STATEMENT
            EXECUTE FUNCTION trax.notify_queued_work();
    END IF;
END$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'work_queue_confirmed_notify'
          AND tgrelid = 'trax.work_queue'::regclass
    ) THEN
        CREATE TRIGGER work_queue_confirmed_notify
            AFTER UPDATE OF confirmed_at ON trax.work_queue
            FOR EACH ROW
            WHEN (OLD.confirmed_at IS NULL AND NEW.confirmed_at IS NOT NULL
                  AND NEW.status = 'queued')
            EXECUTE FUNCTION trax.notify_queued_work();
    END IF;
END$$;
