-- See the Postgres migration of the same name: revision, 1 on insert and one more on every UPDATE,
-- bumped by the database; and ir_hash, the IR hash of the definition that wrote the row, or null.
-- row_id is INTEGER PRIMARY KEY AUTOINCREMENT, so a re-created row never reuses an old row's id.
--
-- SQLite cannot assign NEW in a BEFORE trigger, so the bump is an AFTER UPDATE trigger that writes
-- the row once more. It fires when an UPDATE leaves revision where it was or moves it back, and sets
-- it to OLD.revision + 1. Its own UPDATE moves revision above the value it replaces, which the WHEN
-- clause skips, so it stops there even with recursive triggers on. A writer that moves revision
-- forward itself keeps its value: the column never goes back, though unlike Postgres it can skip.
-- Rows changed by a trigger are not counted in changes(), so a compare-and-swap still reads one row
-- written.
ALTER TABLE snapshot_draft ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

ALTER TABLE snapshot_draft ADD COLUMN ir_hash TEXT NULL;

CREATE TRIGGER IF NOT EXISTS snapshot_draft_revision
    AFTER UPDATE ON snapshot_draft
    FOR EACH ROW
    WHEN NEW.revision <= OLD.revision
BEGIN
    UPDATE snapshot_draft SET revision = OLD.revision + 1 WHERE row_id = NEW.row_id;
END;
