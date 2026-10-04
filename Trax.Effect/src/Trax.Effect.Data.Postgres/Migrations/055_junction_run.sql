-- The steps of a run, in the order it reached them: each junction that ran, each question a routing
-- step asked, and each track a routing step took. Written by AddJunctionEvents from the junction
-- events it publishes, so a finished run's timeline, and a running one's steps so far, can be read
-- back. The model is Trax.Effect.Models.JunctionRun, on IDataContext.JunctionRuns.
--
-- Never an input, an output, a failure's message or anything a decider was shown: names, times,
-- states, how a failure is classified, its exception's type, and for a question the answer the run
-- acted on, which answer_withheld leaves out for a question marked [TraxSensitive].
--
-- A step belongs to its run and goes with it: the cascade keeps every existing delete of metadata
-- (cleanup, manifest pruning) working without knowing this table exists.
--
-- Two brand new enum types (effect/0006), so CREATE TYPE rather than ALTER TYPE, guarded so the
-- script can run again (effect/0014).
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE t.typname = 'junction_run_kind' AND n.nspname = 'trax'
    ) THEN
        CREATE TYPE trax.junction_run_kind AS ENUM ('junction', 'choice', 'score', 'yes_no', 'route');
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE t.typname = 'junction_run_state' AND n.nspname = 'trax'
    ) THEN
        CREATE TYPE trax.junction_run_state AS ENUM ('in_progress', 'completed', 'failed', 'cancelled');
    END IF;
END$$;

CREATE TABLE IF NOT EXISTS trax.junction_run (
    id bigserial PRIMARY KEY,
    metadata_id bigint NOT NULL REFERENCES trax.metadata (id) ON DELETE CASCADE,
    position integer NOT NULL,
    kind trax.junction_run_kind NOT NULL,
    name text NOT NULL,
    state trax.junction_run_state NOT NULL,
    started_at timestamptz NOT NULL,
    ended_at timestamptz,
    failure_class trax.failure_class,
    failure_exception text,
    question_key text,
    answer text,
    confidence double precision,
    replayed boolean NOT NULL DEFAULT false,
    answer_withheld boolean NOT NULL DEFAULT false,
    CONSTRAINT uq_junction_run_position UNIQUE (metadata_id, position)
);
