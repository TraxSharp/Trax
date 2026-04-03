-- Consolidated SQLite schema for Trax
-- Equivalent to Postgres migrations 001-033

CREATE TABLE IF NOT EXISTS manifest_group (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    max_active_jobs INTEGER,
    priority INTEGER NOT NULL DEFAULT 0,
    is_enabled INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    CONSTRAINT uq_manifest_group_name UNIQUE (name)
);

CREATE TABLE IF NOT EXISTS manifest (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    external_id TEXT NOT NULL,
    name TEXT NOT NULL,
    property_type TEXT,
    properties TEXT,
    is_enabled INTEGER NOT NULL DEFAULT 1,
    schedule_type TEXT NOT NULL DEFAULT 'none',
    cron_expression TEXT,
    interval_seconds INTEGER,
    max_retries INTEGER NOT NULL DEFAULT 3,
    timeout_seconds INTEGER,
    last_successful_run TEXT,
    depends_on_manifest_id INTEGER REFERENCES manifest(id) ON DELETE SET NULL,
    priority INTEGER NOT NULL DEFAULT 0,
    manifest_group_id INTEGER NOT NULL REFERENCES manifest_group(id),
    misfire_policy TEXT NOT NULL DEFAULT 'fire_once_now',
    misfire_threshold_seconds INTEGER,
    scheduled_at TEXT,
    exclusions TEXT,
    variance_seconds INTEGER,
    next_scheduled_run TEXT
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_manifest_external_id ON manifest (external_id);
CREATE INDEX IF NOT EXISTS manifest_name_idx ON manifest (name);
CREATE INDEX IF NOT EXISTS manifest_scheduling_idx ON manifest (is_enabled, schedule_type) WHERE is_enabled = 1;
CREATE INDEX IF NOT EXISTS ix_manifest_depends_on ON manifest (depends_on_manifest_id) WHERE depends_on_manifest_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_manifest_manifest_group_id ON manifest (manifest_group_id);

CREATE TABLE IF NOT EXISTS metadata (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    parent_id INTEGER REFERENCES metadata(id),
    external_id TEXT NOT NULL,
    hangfire_job_id TEXT,
    name TEXT NOT NULL,
    executor TEXT,
    train_state TEXT NOT NULL DEFAULT 'pending',
    database_changes INTEGER NOT NULL DEFAULT 0,
    failure_junction TEXT,
    failure_reason TEXT,
    failure_exception TEXT,
    stack_trace TEXT,
    start_time TEXT NOT NULL,
    end_time TEXT,
    input TEXT,
    output TEXT,
    manifest_id INTEGER REFERENCES manifest(id) ON DELETE RESTRICT,
    scheduled_time TEXT,
    cancel_requested INTEGER NOT NULL DEFAULT 0,
    junction_started_at TEXT,
    currently_running_junction TEXT,
    host_name TEXT,
    host_environment TEXT,
    host_instance_id TEXT,
    host_labels TEXT
);

CREATE INDEX IF NOT EXISTS ix_metadata_manifest_id ON metadata (manifest_id);
CREATE INDEX IF NOT EXISTS ix_metadata_name_train_state ON metadata (name, train_state);
CREATE INDEX IF NOT EXISTS ix_metadata_train_state_start_time ON metadata (train_state, start_time) WHERE train_state IN ('pending', 'in_progress');
CREATE INDEX IF NOT EXISTS ix_metadata_start_time_desc ON metadata (start_time DESC);
CREATE INDEX IF NOT EXISTS ix_metadata_manifest_id_train_state ON metadata (manifest_id, train_state) WHERE train_state IN ('pending', 'in_progress');
CREATE INDEX IF NOT EXISTS ix_metadata_end_time_desc ON metadata (end_time DESC) WHERE end_time IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_metadata_host_name ON metadata (host_name);
CREATE INDEX IF NOT EXISTS ix_metadata_host_environment ON metadata (host_environment);
CREATE INDEX IF NOT EXISTS ix_metadata_active_capacity ON metadata (train_state, manifest_id) WHERE train_state IN ('pending', 'in_progress');
CREATE INDEX IF NOT EXISTS ix_metadata_cleanup ON metadata (name, start_time) WHERE train_state IN ('completed', 'failed', 'cancelled');

CREATE TABLE IF NOT EXISTS log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metadata_id INTEGER,
    event_id INTEGER NOT NULL,
    level TEXT NOT NULL,
    message TEXT NOT NULL,
    category TEXT NOT NULL,
    exception TEXT,
    stack_trace TEXT
);

CREATE INDEX IF NOT EXISTS ix_log_metadata_id ON log (metadata_id);

CREATE TABLE IF NOT EXISTS dead_letter (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    manifest_id INTEGER NOT NULL REFERENCES manifest(id) ON DELETE RESTRICT,
    dead_lettered_at TEXT NOT NULL DEFAULT (datetime('now')),
    status TEXT NOT NULL DEFAULT 'awaiting_intervention',
    resolved_at TEXT,
    resolution_note TEXT,
    reason TEXT NOT NULL,
    retry_count_at_dead_letter INTEGER NOT NULL,
    retry_metadata_id INTEGER REFERENCES metadata(id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS dead_letter_manifest_id_idx ON dead_letter (manifest_id);
CREATE INDEX IF NOT EXISTS dead_letter_status_idx ON dead_letter (status) WHERE status = 'awaiting_intervention';
CREATE INDEX IF NOT EXISTS dead_letter_dead_lettered_at_idx ON dead_letter (dead_lettered_at);

CREATE TABLE IF NOT EXISTS work_queue (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    external_id TEXT NOT NULL,
    train_name TEXT NOT NULL,
    input TEXT,
    input_type_name TEXT,
    status TEXT NOT NULL DEFAULT 'queued',
    manifest_id INTEGER REFERENCES manifest(id) ON DELETE RESTRICT,
    metadata_id INTEGER REFERENCES metadata(id) ON DELETE RESTRICT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    dispatched_at TEXT,
    priority INTEGER NOT NULL DEFAULT 0,
    scheduled_at TEXT,
    dispatch_attempts INTEGER NOT NULL DEFAULT 0,
    dead_letter_id INTEGER REFERENCES dead_letter(id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_work_queue_external_id ON work_queue (external_id);
CREATE INDEX IF NOT EXISTS ix_work_queue_status ON work_queue (status) WHERE status = 'queued';
CREATE INDEX IF NOT EXISTS ix_work_queue_manifest_id ON work_queue (manifest_id);
CREATE INDEX IF NOT EXISTS ix_work_queue_status_priority ON work_queue (status, priority DESC, created_at ASC) WHERE status = 'queued';
CREATE UNIQUE INDEX IF NOT EXISTS ix_work_queue_unique_queued_manifest ON work_queue (manifest_id) WHERE status = 'queued' AND manifest_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_work_queue_scheduled_at ON work_queue (scheduled_at) WHERE status = 'queued' AND scheduled_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_work_queue_manifest_id_status_queued ON work_queue (manifest_id, status) WHERE status = 'queued';

CREATE TABLE IF NOT EXISTS background_job (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metadata_id INTEGER NOT NULL,
    input TEXT,
    input_type TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    fetched_at TEXT,
    priority INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_background_job_unfetched ON background_job (priority DESC, created_at ASC) WHERE fetched_at IS NULL;
