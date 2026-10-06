-- Decisions a train made at run time: each question it asked a decider (a typed decision model, a
-- rule table, a cascade of both), the answer it acted on, and the track it took. Written by
-- AddDecisionRecording as each decision is made, before the run acts on it, and read back to replay
-- a requeued run's decisions so it takes the tracks the original took instead of asking again. The model is
-- Trax.Effect.Models.RecordedDecision, on IDataContext.RecordedDecisions. routes holds every track a
-- routing step took on the decision, in order, since more than one step can route on one decision.
-- refused holds why the run would not act on a decider's answer (missing, or not fitting the
-- question), with that answer in answer, or answer null when there was none. A refused row is never
-- replayed.
--
-- A decision belongs to its run and goes with it: the cascade keeps every existing delete of
-- metadata (cleanup, manifest pruning) working without knowing this table exists.
CREATE TABLE IF NOT EXISTS trax.decision (
    id bigserial PRIMARY KEY,
    metadata_id bigint NOT NULL REFERENCES trax.metadata (id) ON DELETE CASCADE,
    question_key text NOT NULL,
    occurrence integer NOT NULL,
    fingerprint text NOT NULL,
    kind text NOT NULL,
    question jsonb NOT NULL,
    answer jsonb,
    model text,
    decider text,
    replayed boolean NOT NULL DEFAULT false,
    shadows jsonb,
    routes jsonb,
    decided_at timestamptz NOT NULL DEFAULT now(),
    refused text,
    CONSTRAINT ck_decision_answer_or_refused CHECK (answer IS NOT NULL OR refused IS NOT NULL),
    CONSTRAINT uq_decision_run_question UNIQUE (metadata_id, question_key, occurrence)
);

-- Whether the run started on a host recording its decisions, set on its first write. A replay must
-- tell a run that reached no questions (recorded, with nothing to replay) from one whose decisions
-- were never recorded, whose answers it cannot know.
ALTER TABLE trax.metadata ADD COLUMN IF NOT EXISTS decisions_recorded boolean NOT NULL DEFAULT false;

-- Which run's decisions a run replays. Set by a requeue on the queued entry and carried to the
-- run's metadata at dispatch. Not a foreign key: the original may be deleted before the requeue
-- runs, and the requeued run then fails rather than asking afresh. A question the original never
-- reached is asked afresh.
ALTER TABLE trax.work_queue ADD COLUMN IF NOT EXISTS replay_decisions_of bigint;
ALTER TABLE trax.metadata ADD COLUMN IF NOT EXISTS replay_decisions_of bigint;

-- The metadata cleanup keeps a run while a queued entry or another run still replays it, and
-- looks that up by these columns. Few rows carry a link, so the indexes are partial; built
-- CONCURRENTLY so the build does not block writes to either table (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_work_queue_replay_decisions_of
    ON trax.work_queue (replay_decisions_of) WHERE replay_decisions_of IS NOT NULL;
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_metadata_replay_decisions_of
    ON trax.metadata (replay_decisions_of) WHERE replay_decisions_of IS NOT NULL;
