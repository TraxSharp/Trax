-- Whether a run that named a run to replay (replay_decisions_of) asked its questions afresh
-- instead, because the replay could not be honoured: only a manifest's retry does, any other run
-- fails. replay_decisions_of is kept as the record of what the run was queued to do. A later
-- replay of the run stops at it instead of following replay_decisions_of to answers the run never
-- acted on.
ALTER TABLE trax.metadata ADD COLUMN IF NOT EXISTS replay_abandoned boolean NOT NULL DEFAULT false;
