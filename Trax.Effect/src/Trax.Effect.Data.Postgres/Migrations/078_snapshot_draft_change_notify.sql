-- Announces every committed write to a state-machine instance (Trax.Effect.StateMachine.Persistence,
-- Trax.Docs/adr/0048), so each host's change listener can push the instance to the clients watching it.
--
-- One notice per row inserted, updated or deleted, carrying the row's row_id and nothing else: the
-- payload is a hint. A listener reads the revision, the state and whether the row still exists from
-- the table, so a notice anyone sends by hand moves nothing, and no context ever travels through the
-- notification queue, which the whole cluster shares. row_id names the
-- incarnation (076): a draft deleted and created again under one id is a new row_id.
--
-- A trigger rather than a call at each write site covers every writer: the draft service, the effect
-- runner, the system path, the outcome hook and reconciler, the sweeper's bulk delete, a host's
-- custom store, raw SQL, and a node still on the previous version. pg_notify inside the writing
-- transaction is delivered when it commits and never when it rolls back, and Postgres folds identical
-- notices sent in one transaction into one, so two writes to one row in one transaction send one.
--
-- AFTER ... FOR EACH ROW, so a bulk delete sends one notice per row it deletes, and the BEFORE UPDATE
-- revision trigger (076) has run first.
--
-- The snapshot change listener in Trax.Effect.StateMachine.Persistence listens on this channel by
-- name. SQLite has no notification channel; there the listener polls the table by revision instead,
-- so this migration has no SQLite counterpart.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
CREATE OR REPLACE FUNCTION trax.notify_snapshot_changed() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM pg_notify('trax_snapshot_changed', OLD.row_id::text);
    ELSE
        PERFORM pg_notify('trax_snapshot_changed', NEW.row_id::text);
    END IF;
    RETURN NULL;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'snapshot_draft_changed_notify'
          AND tgrelid = 'trax.snapshot_draft'::regclass
    ) THEN
        CREATE TRIGGER snapshot_draft_changed_notify
            AFTER INSERT OR UPDATE OR DELETE ON trax.snapshot_draft
            FOR EACH ROW
            EXECUTE FUNCTION trax.notify_snapshot_changed();
    END IF;
END$$;
