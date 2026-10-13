-- An effect pending on its state-machine instance's row, and whether the server put the row in its
-- state (Trax.Effect.StateMachine.Persistence, Trax.Docs/adr/0048, "Effects and server records").
-- Mirrors SnapshotDraft.EffectToken, EffectTrigger, EffectRequestId, EffectLeaseUntil and ServerRecord.
--
-- effect_token: the claimant of the effect pending on the row, or null when none is. Sending an
-- effect-bound event sets it by a conditional write on the row; every other write to the row
-- requires it to be null, so nothing moves or changes the row while the effect runs. The effect's
-- receipt, the move to its target and clearing these columns are one write, guarded by the token.
-- effect_key: the key the pending effect runs under, minted when the row is marked, so a run again
-- after its claimant stopped uses the same key and the next send of the event a new one.
-- effect_trigger and effect_request_id: the event whose effect is pending, and the client request
-- that sent it, so whoever finishes the effect records it as that request's answer.
-- effect_lease_until: when the claimant's lease ends, on the database's clock. Past it, the next
-- send or the reconciler takes the mark over and runs the effect again under the same key.
--
-- server_record: true when the server put the row in its state (an effect, an invoked train's
-- outcome, an event gated by a role or a policy), false when a client did, carried over by a timer.
-- It decides what discard refuses, what the draft sweep keeps and what the draft cap counts. Null on
-- a row written before the column existed, or by host code through the store, which is judged by
-- its state as before: kept when its state is one the earlier rule kept.
--
-- ix_snapshot_draft_effect_pending: the reconciler's query for marks whose lease has passed. Partial,
-- so it holds only the rows with a pending effect.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS effect_token uuid NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS effect_key text NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS effect_trigger text NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS effect_request_id text NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS effect_lease_until timestamptz NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS server_record boolean NULL;

-- CONCURRENTLY so drafts keep being saved and advanced while it builds (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_snapshot_draft_effect_pending
    ON trax.snapshot_draft (effect_lease_until, row_id)
    WHERE effect_token IS NOT NULL;
