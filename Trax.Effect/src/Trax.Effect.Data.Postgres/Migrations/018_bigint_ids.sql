-- Migration 018: Widen all entity IDs and FKs from integer to bigint
--
-- WARNING: ALTER COLUMN TYPE from integer to bigint requires a table rewrite,
-- which acquires an ACCESS EXCLUSIVE lock. For tables with millions of rows,
-- this may take seconds to minutes. Plan for a maintenance window.
-- Run VACUUM ANALYZE on affected tables after migration.

-- ── metadata (identity column) ──────────────────────────────────────
ALTER TABLE trax.metadata ALTER COLUMN id TYPE bigint;
ALTER TABLE trax.metadata ALTER COLUMN parent_id TYPE bigint;
ALTER TABLE trax.metadata ALTER COLUMN manifest_id TYPE bigint;

-- ── log (identity column) ───────────────────────────────────────────
ALTER TABLE trax.log ALTER COLUMN id TYPE bigint;
ALTER TABLE trax.log ALTER COLUMN metadata_id TYPE bigint;

-- ── manifest (identity column) ──────────────────────────────────────
ALTER TABLE trax.manifest ALTER COLUMN id TYPE bigint;
ALTER TABLE trax.manifest ALTER COLUMN manifest_group_id TYPE bigint;
ALTER TABLE trax.manifest ALTER COLUMN depends_on_manifest_id TYPE bigint;

-- ── manifest_group (serial → also alter sequence) ───────────────────
ALTER TABLE trax.manifest_group ALTER COLUMN id TYPE bigint;
ALTER SEQUENCE trax.manifest_group_id_seq AS bigint;

-- ── work_queue (serial → also alter sequence) ───────────────────────
ALTER TABLE trax.work_queue ALTER COLUMN id TYPE bigint;
ALTER SEQUENCE trax.work_queue_id_seq AS bigint;
ALTER TABLE trax.work_queue ALTER COLUMN manifest_id TYPE bigint;
ALTER TABLE trax.work_queue ALTER COLUMN metadata_id TYPE bigint;

-- ── dead_letter (identity column) ───────────────────────────────────
ALTER TABLE trax.dead_letter ALTER COLUMN id TYPE bigint;
ALTER TABLE trax.dead_letter ALTER COLUMN manifest_id TYPE bigint;
ALTER TABLE trax.dead_letter ALTER COLUMN retry_metadata_id TYPE bigint;

-- ── background_job (id is already bigserial, only FK needs change) ──
ALTER TABLE trax.background_job ALTER COLUMN metadata_id TYPE bigint;
