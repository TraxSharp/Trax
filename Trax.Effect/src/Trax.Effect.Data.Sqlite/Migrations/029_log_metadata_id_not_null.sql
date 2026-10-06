-- See the Postgres migration of the same name. SQLite cannot change a column's nullability in place,
-- so the table is rebuilt with its columns in the same order, every row copied across with NULL as 0,
-- and its index built again.
CREATE TABLE log_not_null (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metadata_id INTEGER NOT NULL DEFAULT 0,
    event_id INTEGER NOT NULL,
    level TEXT NOT NULL,
    message TEXT NOT NULL,
    category TEXT NOT NULL,
    exception TEXT,
    stack_trace TEXT
);

INSERT INTO log_not_null (id, metadata_id, event_id, level, message, category, exception, stack_trace)
SELECT id, COALESCE(metadata_id, 0), event_id, level, message, category, exception, stack_trace
FROM log;

DROP TABLE log;

ALTER TABLE log_not_null RENAME TO log;

CREATE INDEX IF NOT EXISTS ix_log_metadata_id ON log (metadata_id);
