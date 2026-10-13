-- Numbers every write to a state-machine instance (Trax.Effect.StateMachine.Persistence,
-- Trax.Docs/adr/0048). Mirrors SnapshotDraft.Revision and SnapshotDraft.IrHash.
--
-- revision: 1 when the row is inserted, and one more on every UPDATE of it. The database does the
-- bump, in a BEFORE UPDATE trigger that sets it from the row being replaced, so every writer is
-- covered without remembering to: Trax's own paths, a host's custom store or raw SQL, and a node
-- still running the previous version during a rollout. A value a writer puts in the column itself is
-- overwritten. A delete followed by an insert under the same id is a new row, with a new row_id from
-- the sequence (never reused), and starts at 1 again: row_id names the incarnation, and revisions are
-- ordered only within one. Existing rows read 1; the constant default is kept in the catalog, so the
-- table is not rewritten (effect/0014).
--
-- ir_hash: the hash of the machine definition's IR that wrote the row (IMachine.SchemaHash), or null
-- for a machine with no exportable IR and for a writer that does not record it. Server-only.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS revision bigint NOT NULL DEFAULT 1;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS ir_hash text NULL;

CREATE OR REPLACE FUNCTION trax.snapshot_draft_bump_revision() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    NEW.revision := OLD.revision + 1;
    RETURN NEW;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'snapshot_draft_revision'
          AND tgrelid = 'trax.snapshot_draft'::regclass
    ) THEN
        CREATE TRIGGER snapshot_draft_revision
            BEFORE UPDATE ON trax.snapshot_draft
            FOR EACH ROW
            EXECUTE FUNCTION trax.snapshot_draft_bump_revision();
    END IF;
END$$;
