-- The drain: one row that says every host sharing the database stops starting new work until a time. The model is
-- Trax.Effect.Models.Drain, on IDataContext.Drains. While the row exists and its time is ahead of a host's clock, the
-- job dispatcher dispatches nothing, the manifest manager queues no scheduled run (a schedule due meanwhile is a
-- misfire when the drain ends) and no local worker claims a job. A deploy sets it before it replaces the hosts
-- (trax deploy, a process started with --trax-drain), ends it once they are replaced, and a starting scheduler host
-- ends it too. A deploy that dies leaves a row that lapses at its time.
--
-- id: always 1; the CHECK keeps the table to one row.
-- until: when the drain lapses.
-- started_at: when it was set.
-- started_by: 'deploy', or the principal id of the operator who set it.
--
-- The table is new, so nothing here scans or locks a populated table (effect/0014). Every statement checks before it
-- changes anything, so the script can run again over its own result.
CREATE TABLE IF NOT EXISTS trax.drain (
    id bigint CONSTRAINT pk_drain PRIMARY KEY CONSTRAINT ck_drain_singleton CHECK (id = 1),
    until timestamptz NOT NULL,
    started_at timestamptz NOT NULL,
    started_by text NOT NULL
);
