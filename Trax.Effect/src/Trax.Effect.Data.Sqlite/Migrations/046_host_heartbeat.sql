-- See the Postgres migration of the same name: one row per running host process, rewritten every
-- 15 seconds and deleted a day after its last beat. A boolean is stored as 0 or 1.
CREATE TABLE IF NOT EXISTS host_heartbeat (
    instance_id TEXT PRIMARY KEY,
    host_name TEXT NOT NULL,
    host_environment TEXT NOT NULL,
    trax_version TEXT NOT NULL,
    started_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_heartbeat_at TEXT NOT NULL DEFAULT (datetime('now')),
    runs_dispatcher INTEGER NOT NULL DEFAULT 0,
    runs_manifest_manager INTEGER NOT NULL DEFAULT 0,
    worker_slots INTEGER,
    worker_slots_in_use INTEGER
);

CREATE INDEX IF NOT EXISTS ix_host_heartbeat_last_heartbeat_at
    ON host_heartbeat (last_heartbeat_at);
