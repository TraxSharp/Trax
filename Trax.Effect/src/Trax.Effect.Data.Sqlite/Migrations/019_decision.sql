-- See the Postgres migration of the same name.
CREATE TABLE IF NOT EXISTS decision (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metadata_id INTEGER NOT NULL REFERENCES metadata (id) ON DELETE CASCADE,
    question_key TEXT NOT NULL,
    occurrence INTEGER NOT NULL,
    fingerprint TEXT NOT NULL,
    kind TEXT NOT NULL,
    question TEXT NOT NULL,
    answer TEXT,
    model TEXT,
    decider TEXT,
    replayed INTEGER NOT NULL DEFAULT 0,
    shadows TEXT,
    routes TEXT,
    decided_at TEXT NOT NULL,
    refused TEXT,
    CHECK (answer IS NOT NULL OR refused IS NOT NULL),
    UNIQUE (metadata_id, question_key, occurrence)
);

ALTER TABLE metadata ADD COLUMN decisions_recorded INTEGER NOT NULL DEFAULT 0;
ALTER TABLE work_queue ADD COLUMN replay_decisions_of INTEGER;
ALTER TABLE metadata ADD COLUMN replay_decisions_of INTEGER;
CREATE INDEX IF NOT EXISTS ix_work_queue_replay_decisions_of
    ON work_queue (replay_decisions_of) WHERE replay_decisions_of IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_metadata_replay_decisions_of
    ON metadata (replay_decisions_of) WHERE replay_decisions_of IS NOT NULL;
