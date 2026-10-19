-- See the Postgres migration of the same name: a state-machine instance's history, one row for every snapshot the
-- store writes, deleted with the instance's row by an AFTER DELETE trigger, as on Postgres. The unique index on
-- draft_row_id and revision serves the trigger's delete and the newest-first read of one instance's history.
--
-- kind is the integer of SnapshotTransitionKind (0 start, 1 command, 2 save, 3 timer, 4 outcome); actor the integer
-- of SnapshotTransitionActor (0 owner, 1 host, 2 machine). occurred_at is fixed-width UTC text, as every
-- DateTimeOffset column on SQLite (017).
CREATE TABLE IF NOT EXISTS snapshot_transition (
    id INTEGER NOT NULL CONSTRAINT pk_snapshot_transition PRIMARY KEY AUTOINCREMENT,
    draft_row_id INTEGER NOT NULL,
    revision INTEGER NOT NULL,
    kind INTEGER NOT NULL,
    trigger TEXT NULL,
    actor INTEGER NOT NULL,
    from_state TEXT NULL,
    to_state TEXT NOT NULL,
    occurred_at TEXT NOT NULL,
    invoke_token TEXT NULL,
    UNIQUE (draft_row_id, revision)
);

CREATE TRIGGER IF NOT EXISTS snapshot_draft_history
    AFTER DELETE ON snapshot_draft
    FOR EACH ROW
BEGIN
    DELETE FROM snapshot_transition WHERE draft_row_id = OLD.row_id;
END;
