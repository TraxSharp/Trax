-- The parent of a state-machine instance another machine's state spawned (Trax.Effect.StateMachine.Persistence,
-- Trax.Docs/adr/0052). Mirrors SnapshotDraft.SpawnedBy.
--
-- spawned_by: the invoke token of the parent row whose spawning state started this one, or null for an instance
-- nothing spawned. The parent holds the same token in invoke_token while it waits; the child's end is applied to the
-- parent that still holds it, and leaving the spawning state deletes the child. Server-only, set on insert and never
-- changed.
--
-- ix_snapshot_draft_spawned_by: finds a parent's child by its token. Partial, so it holds only spawned rows.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS spawned_by text NULL;

-- CONCURRENTLY so drafts keep being saved and advanced while it builds (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_snapshot_draft_spawned_by
    ON trax.snapshot_draft (spawned_by)
    WHERE spawned_by IS NOT NULL;
