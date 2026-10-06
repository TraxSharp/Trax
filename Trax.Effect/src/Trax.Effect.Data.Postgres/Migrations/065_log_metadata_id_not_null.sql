-- log.metadata_id has been nullable since 002, but the model maps it as a plain long and the log
-- writer always stores 0 (no row is tied to its run). A row written without the column, by hand or by
-- another tool, held NULL, and every read of a page containing it failed to materialise the row: the
-- dashboard's log grid and the GraphQL logs query both broke on it. The column now says what the model
-- says: never null, 0 when no run is named. Existing NULLs become 0.
--
-- SET NOT NULL scans the table under an exclusive lock unless a validated check already proves it,
-- so the check is added NOT VALID (no scan), validated under a lock that lets writes continue, used
-- by SET NOT NULL to skip its own scan, and dropped. Each step is guarded so the script runs again
-- over its own result.
ALTER TABLE trax.log ALTER COLUMN metadata_id SET DEFAULT 0;

UPDATE trax.log SET metadata_id = 0 WHERE metadata_id IS NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'trax' AND table_name = 'log' AND column_name = 'metadata_id'
          AND is_nullable = 'YES'
    ) AND NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_log_metadata_id_not_null' AND conrelid = 'trax.log'::regclass
    ) THEN
        ALTER TABLE trax.log
            ADD CONSTRAINT ck_log_metadata_id_not_null CHECK (metadata_id IS NOT NULL) NOT VALID;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_log_metadata_id_not_null' AND conrelid = 'trax.log'::regclass
          AND NOT convalidated
    ) THEN
        ALTER TABLE trax.log VALIDATE CONSTRAINT ck_log_metadata_id_not_null;
    END IF;
END $$;

ALTER TABLE trax.log ALTER COLUMN metadata_id SET NOT NULL;

ALTER TABLE trax.log DROP CONSTRAINT IF EXISTS ck_log_metadata_id_not_null;
