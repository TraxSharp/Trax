-- See the Postgres migration of the same name. kind and state store the integer values of
-- JunctionRunKind and JunctionRunState, and failure_class that of FailureClass (effect/0006).
CREATE TABLE IF NOT EXISTS junction_run (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metadata_id INTEGER NOT NULL REFERENCES metadata (id) ON DELETE CASCADE,
    position INTEGER NOT NULL,
    kind INTEGER NOT NULL,
    name TEXT NOT NULL,
    state INTEGER NOT NULL,
    started_at TEXT NOT NULL,
    ended_at TEXT,
    failure_class INTEGER,
    failure_exception TEXT,
    question_key TEXT,
    answer TEXT,
    confidence REAL,
    replayed INTEGER NOT NULL DEFAULT 0,
    answer_withheld INTEGER NOT NULL DEFAULT 0,
    UNIQUE (metadata_id, position)
);
