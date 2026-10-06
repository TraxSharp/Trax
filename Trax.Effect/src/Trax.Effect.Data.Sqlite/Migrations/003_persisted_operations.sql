-- Persisted GraphQL operations: server-managed manifest mapping operation IDs
-- to documents. See the Postgres counterpart (035_persisted_operations.sql)
-- for the full design rationale; this is the SQLite-shape mirror used by
-- the integration tests.

CREATE TABLE IF NOT EXISTS persisted_operation (
    -- '' is the sentinel for "no tenant"; the C# storage layer normalizes
    -- null<->'' at the boundary because SQL forbids NULLs in PK columns.
    tenant_key        TEXT    NOT NULL DEFAULT '',
    id                TEXT    NOT NULL,
    operation_name    TEXT    NOT NULL,
    version           INTEGER NOT NULL,
    document          TEXT    NOT NULL,
    shape_fingerprint TEXT    NOT NULL,
    is_active         INTEGER NOT NULL DEFAULT 1,
    deprecation_reason TEXT,
    description       TEXT,
    created_at        TEXT    NOT NULL DEFAULT (datetime('now')),
    updated_at        TEXT    NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (tenant_key, id)
);

CREATE INDEX IF NOT EXISTS persisted_operation_active_idx
    ON persisted_operation (tenant_key, id)
    WHERE is_active = 1;

CREATE INDEX IF NOT EXISTS persisted_operation_name_idx
    ON persisted_operation (tenant_key, operation_name);


CREATE TABLE IF NOT EXISTS persisted_operation_history (
    history_id INTEGER PRIMARY KEY AUTOINCREMENT,
    tenant_key TEXT NOT NULL DEFAULT '',
    id TEXT NOT NULL,
    document TEXT NOT NULL,
    shape_fingerprint TEXT NOT NULL,
    change_type TEXT NOT NULL,
    changed_at TEXT NOT NULL DEFAULT (datetime('now')),
    changed_reason TEXT
);

CREATE INDEX IF NOT EXISTS persisted_operation_history_id_idx
    ON persisted_operation_history (tenant_key, id, changed_at DESC);
