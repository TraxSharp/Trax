-- Lets an operator turn off the snapshot change notices of 078 (Trax.Effect.StateMachine.Persistence,
-- Trax.Docs/adr/0048) for a whole database, without a migration of their own.
--
-- A transaction that sends a NOTIFY takes one lock that every notifying transaction in the cluster
-- shares, and holds it through its commit. With 078's trigger every write to trax.snapshot_draft is
-- such a transaction, so concurrent snapshot writes commit one at a time, together with the work
-- queue's (069) and the invoked runs' (073) notices. A host that cannot pay for that sets
-- StateMachineOptions.ChangeFeed to Poll, which reads the watched rows by revision instead (as on
-- SQLite), and once every host polls the database stops sending the notices with
--
--     ALTER DATABASE <name> SET trax.snapshot_notify = 'off';
--
-- (new sessions only). The condition is read in the session that writes, and a host that listens can
-- check only the database's setting, so that is the one supported switch: a role setting, a
-- connection string's Options=-c trax.snapshot_notify=off or a SET LOCAL turns the notices off for
-- some writers only, and a host on Notify never hears their writes. The value 'off' in any case turns
-- the notices off; any other value ('on' is the one to write), or none, keeps them, so a database
-- nobody configures behaves as it did under 078.
--
-- The condition is the trigger's WHEN, so a write with the notices off never calls the function.
-- current_setting(..., true) reads null rather than failing when the setting was never defined.
--
-- The DO block replaces the trigger only while it has no condition, so the script can run again over
-- its own result; the function is 078's and unchanged.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'snapshot_draft_changed_notify'
          AND tgrelid = 'trax.snapshot_draft'::regclass
          AND tgqual IS NOT NULL
    ) THEN
        DROP TRIGGER IF EXISTS snapshot_draft_changed_notify ON trax.snapshot_draft;
        CREATE TRIGGER snapshot_draft_changed_notify
            AFTER INSERT OR UPDATE OR DELETE ON trax.snapshot_draft
            FOR EACH ROW
            WHEN (lower(current_setting('trax.snapshot_notify', true)) IS DISTINCT FROM 'off')
            EXECUTE FUNCTION trax.notify_snapshot_changed();
    END IF;
END$$;
