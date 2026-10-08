-- Delivers the outcome of a run a state machine's invoking state queued (072) back to the machine
-- (Trax.Effect.StateMachine.Persistence, Trax.Docs/adr/0046).
--
-- invoke_output, invoke_output_oversize: the run's output as the machine's OnDone edges read it,
-- written by the run itself in the same UPDATE that records it Completed, so a host applying the
-- outcome after any crash finds it there. Never metadata.output, which is redacted and bounded by
-- host policy. An output past the 64 KiB snapshot cap is not stored; the flag says so, and the
-- invoking state fails. Both stay null/false on every other run. Neither is on any operator view.
--
-- The notify: a run that a machine is waiting on reaching an end (its metadata becomes completed,
-- failed or cancelled, or its still-queued entry is cancelled) wakes the outcome reconciler on every
-- host, instead of leaving it to the next sweep. pg_notify inside the writing transaction is
-- delivered when it commits and never when it rolls back. The payload is the run's external id, the
-- token the waiting snapshot holds. A requeued dispatch failure notifies too; the reconciler finds
-- the entry queued again and applies nothing.
--
-- PostgresInvokedRunListener in Trax.Effect.Data.Postgres listens on this channel by name.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.metadata
    ADD COLUMN IF NOT EXISTS invoke_output text NULL;

ALTER TABLE trax.metadata
    ADD COLUMN IF NOT EXISTS invoke_output_oversize boolean NOT NULL DEFAULT false;

CREATE OR REPLACE FUNCTION trax.notify_invoked_run_ended() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    PERFORM pg_notify('trax_invoked_run_ended', NEW.external_id);
    RETURN NULL;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'metadata_invoked_run_ended_notify'
          AND tgrelid = 'trax.metadata'::regclass
    ) THEN
        CREATE TRIGGER metadata_invoked_run_ended_notify
            AFTER UPDATE OF train_state ON trax.metadata
            FOR EACH ROW
            WHEN (NEW.invoking_machine IS NOT NULL
                  AND OLD.train_state IS DISTINCT FROM NEW.train_state
                  AND NEW.train_state IN ('completed', 'failed', 'cancelled'))
            EXECUTE FUNCTION trax.notify_invoked_run_ended();
    END IF;
END$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'work_queue_invoked_run_cancelled_notify'
          AND tgrelid = 'trax.work_queue'::regclass
    ) THEN
        CREATE TRIGGER work_queue_invoked_run_cancelled_notify
            AFTER UPDATE OF status ON trax.work_queue
            FOR EACH ROW
            WHEN (NEW.invoking_machine IS NOT NULL
                  AND OLD.status IS DISTINCT FROM NEW.status
                  AND NEW.status = 'cancelled')
            EXECUTE FUNCTION trax.notify_invoked_run_ended();
    END IF;
END$$;
