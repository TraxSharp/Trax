-- Who started a run, as Postgres 082 and 083 record it: the hex SHA-256 of a tag and the starter's id ("p\0" and the
-- qualified principal id for a caller, "u\0" and the user key for an instance's owner), on the work queue entry and the
-- run's metadata, and an index over the live invoked runs of one user. Enums are their integers: invoking_owner_kind 0
-- is a user, train_state 0 is pending and 3 in progress.
ALTER TABLE work_queue ADD COLUMN starter_hash TEXT NULL;

ALTER TABLE metadata ADD COLUMN starter_hash TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_metadata_live_invoked_starter
    ON metadata (starter_hash)
    WHERE starter_hash IS NOT NULL AND invoking_owner_kind = 0 AND train_state IN (0, 3);
