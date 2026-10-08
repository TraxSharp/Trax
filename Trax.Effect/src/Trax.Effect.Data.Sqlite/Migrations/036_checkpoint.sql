-- See the Postgres migration of the same name: the state a train declared with
-- Checkpoint<TState>(), one row per run and node and deleted with its run, and the resume links on
-- the work queue entry and on the run's metadata. The unique index on metadata_id and node_id
-- serves the cascade and every lookup of a run's rows. WorkQueueStatus.Queued is stored as 0 (see 014).
CREATE TABLE IF NOT EXISTS checkpoint (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metadata_id INTEGER NOT NULL REFERENCES metadata (id) ON DELETE CASCADE,
    node_id TEXT NOT NULL,
    branch_path TEXT,
    state_type TEXT NOT NULL,
    state TEXT NOT NULL,
    tracks TEXT NOT NULL DEFAULT '[]',
    chain_hash TEXT NOT NULL,
    state_fingerprint TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE (metadata_id, node_id)
);

ALTER TABLE work_queue ADD COLUMN resume_from INTEGER;

ALTER TABLE work_queue ADD COLUMN resume_at TEXT;

ALTER TABLE metadata ADD COLUMN resume_from INTEGER;

ALTER TABLE metadata ADD COLUMN resume_at TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS ix_work_queue_unique_queued_resume
    ON work_queue (resume_from)
    WHERE status = 0 AND resume_from IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_metadata_resume_from
    ON metadata (resume_from) WHERE resume_from IS NOT NULL;
