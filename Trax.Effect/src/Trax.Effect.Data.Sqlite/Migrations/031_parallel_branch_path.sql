-- See the Postgres migration of the same name. SQLite cannot drop a table's UNIQUE constraint in
-- place, so the decision table is rebuilt with branch_path in its key, its columns in the same order
-- with branch_path last, every row copied across with an empty branch, and its index built again.
CREATE TABLE decision_by_branch (
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
    state_hash TEXT,
    branch_path TEXT NOT NULL DEFAULT '',
    CHECK (answer IS NOT NULL OR refused IS NOT NULL),
    UNIQUE (metadata_id, branch_path, question_key, occurrence)
);

INSERT INTO decision_by_branch (
    id, metadata_id, question_key, occurrence, fingerprint, kind, question, answer, model, decider,
    replayed, shadows, routes, decided_at, refused, state_hash, branch_path
)
SELECT
    id, metadata_id, question_key, occurrence, fingerprint, kind, question, answer, model, decider,
    replayed, shadows, routes, decided_at, refused, state_hash, ''
FROM decision;

DROP TABLE decision;

ALTER TABLE decision_by_branch RENAME TO decision;

CREATE INDEX IF NOT EXISTS ix_decision_metadata_id_id ON decision (metadata_id, id);

ALTER TABLE junction_run ADD COLUMN branch_path TEXT;
