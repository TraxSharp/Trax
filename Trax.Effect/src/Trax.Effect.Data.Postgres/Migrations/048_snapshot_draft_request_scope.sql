-- Scopes a state-machine draft's idempotent replay to the request it recorded
-- (Trax.Effect.StateMachine.Persistence). last_request_id alone let an id reused for a different trigger
-- replay an unrelated advance, so the draft now also records the trigger that request fired and the state
-- it fired from. Columns mirror SnapshotRecord (Entities.cs) exactly.
--
-- Both are nullable and nothing backfills them: a row written before this migration has no recorded
-- trigger, so a retry of its last request is refused as a reused id instead of being replayed. That costs
-- a client one reload, and it never replays a request whose trigger nobody recorded.
ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS last_request_trigger TEXT NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS last_request_from_state TEXT NULL;
