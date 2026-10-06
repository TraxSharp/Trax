-- Five columns were `timestamp without time zone` while every other Trax timestamp is timestamptz:
-- work_queue.created_at and dispatched_at (011), manifest_group.created_at and updated_at (014),
-- and manifest.next_scheduled_run (030). EF sends a UTC DateTime as timestamptz, and Postgres
-- converts that into a plain timestamp with the session's TimeZone, so a session that was not in
-- UTC stored a different wall-clock time: New York four or five hours back, Berlin one or two
-- ahead. Variance schedules then fired hours early or late, and dispatch order mixed the zones.
--
-- Every value Trax wrote from a UTC session is a UTC wall-clock time, so each stored value is read
-- as the UTC instant it meant. A row written from a session in another zone was already shifted
-- when it was stored, and nothing can tell which rows those were.
--
-- Each block first sets its transaction's TimeZone to UTC and then changes the type with no USING.
-- Postgres converts a plain timestamp by the session's zone, so that reads every value as UTC, and
-- in a UTC session a timestamp-to-timestamptz change keeps the table's storage as it is: no
-- rewrite, only a catalog change under a brief ACCESS EXCLUSIVE lock. A USING clause, or a session
-- in another zone, would rewrite the whole table under that lock instead. set_config(..., true)
-- lasts until the block's own transaction ends, so this holds even on a connection whose zone is
-- not UTC (CreateEngineWithEmbeddedScripts run directly), and leaves the session as it found it.
--
-- Each block checks the column's current type first, so a run that stopped partway resumes where
-- it left off and a second run changes nothing. The defaults move to now() in the same statement,
-- because `now() AT TIME ZONE 'utc'` is a plain timestamp that a timestamptz column would read in
-- the session's zone.
DO $$
BEGIN
    PERFORM set_config('TimeZone', 'UTC', true);
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'trax' AND table_name = 'work_queue'
          AND column_name = 'created_at' AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE trax.work_queue
            ALTER COLUMN created_at DROP DEFAULT,
            ALTER COLUMN created_at TYPE timestamptz,
            ALTER COLUMN created_at SET DEFAULT now(),
            ALTER COLUMN dispatched_at TYPE timestamptz;
    END IF;
END$$;

DO $$
BEGIN
    PERFORM set_config('TimeZone', 'UTC', true);
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'trax' AND table_name = 'manifest_group'
          AND column_name = 'created_at' AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE trax.manifest_group
            ALTER COLUMN created_at DROP DEFAULT,
            ALTER COLUMN created_at TYPE timestamptz,
            ALTER COLUMN created_at SET DEFAULT now(),
            ALTER COLUMN updated_at DROP DEFAULT,
            ALTER COLUMN updated_at TYPE timestamptz,
            ALTER COLUMN updated_at SET DEFAULT now();
    END IF;
END$$;

DO $$
BEGIN
    PERFORM set_config('TimeZone', 'UTC', true);
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'trax' AND table_name = 'manifest'
          AND column_name = 'next_scheduled_run' AND data_type = 'timestamp without time zone'
    ) THEN
        ALTER TABLE trax.manifest
            ALTER COLUMN next_scheduled_run TYPE timestamptz;
    END IF;
END$$;

-- background_job.created_at was already timestamptz, but its default had the same plain-timestamp
-- expression, so a row defaulted from a non-UTC session was stamped hours off.
ALTER TABLE trax.background_job ALTER COLUMN created_at SET DEFAULT now();
