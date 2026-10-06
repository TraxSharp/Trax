-- What kind of failure a run hit, as classified where it happened.
--
-- A brand new enum type, so this is CREATE TYPE rather than ALTER TYPE: a value added to an
-- existing enum is unreadable to processes that do not know it yet, which is why a new work queue
-- status was ruled out earlier. Nothing reads this column until code that knows the type is
-- deployed, so a rolling deploy is safe.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE t.typname = 'failure_class' AND n.nspname = 'trax'
    ) THEN
        CREATE TYPE trax.failure_class AS ENUM ('unclassified', 'transient', 'conflict', 'permanent');
    END IF;
END$$;

ALTER TABLE trax.metadata
    ADD COLUMN IF NOT EXISTS failure_class trax.failure_class NOT NULL DEFAULT 'unclassified';
