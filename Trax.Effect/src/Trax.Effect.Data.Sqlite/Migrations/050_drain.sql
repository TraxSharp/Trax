-- See the Postgres migration of the same name: the one row that says every host stops starting new work until a time.
-- A time is stored as text in UTC.
CREATE TABLE IF NOT EXISTS drain (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    until TEXT NOT NULL,
    started_at TEXT NOT NULL,
    started_by TEXT NOT NULL
);
