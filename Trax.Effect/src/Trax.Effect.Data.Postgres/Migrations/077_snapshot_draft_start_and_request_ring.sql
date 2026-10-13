-- A server-minted start and a ring of recent client requests on a state-machine instance
-- (Trax.Effect.StateMachine.Persistence, Trax.Docs/adr/0048). Mirrors SnapshotDraft.StartRequestId,
-- SnapshotDraft.StartFingerprint and SnapshotDraft.RecentRequests.
--
-- start_request_id: the client's request id of the start that created the row, or null for a row a save
-- or host code created. Unique per owner kind, user and machine, so a retried start finds the instance it
-- created and racing retries create one row.
--
-- start_fingerprint: the hash of that start's input, so the same request id with a different input is
-- told apart and refused.
--
-- recent_requests: the row's last sixteen client requests, oldest first, each with its request id, a
-- fingerprint of its trigger and input, the state it fired from, the revision it produced and the
-- snapshot it was answered with. A retry is answered with what it was answered with then. Server writes
-- (outcomes, timers) never change it. Null on a row no client request has advanced since the column was
-- added, where the last_request_* columns still apply.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS start_request_id text NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS start_fingerprint text NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS recent_requests jsonb NULL;

-- CONCURRENTLY so drafts keep being saved and advanced while it builds (effect/0014).
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_snapshot_draft_start_request
    ON trax.snapshot_draft (owner_kind, user_key, machine, start_request_id)
    WHERE start_request_id IS NOT NULL;
