-- See the Postgres migration of the same name: the effect pending on an instance's row (its claimant,
-- key, event, request and lease), whether the server put the row in its state, and the partial index the
-- reconciler reads for marks whose lease has passed. A Guid is stored as its uppercase text form and
-- a DateTimeOffset as fixed-width UTC text, as every such column on SQLite, so text order is time
-- order; a boolean as 0 or 1.
ALTER TABLE snapshot_draft ADD COLUMN effect_token TEXT NULL;

ALTER TABLE snapshot_draft ADD COLUMN effect_key TEXT NULL;

ALTER TABLE snapshot_draft ADD COLUMN effect_trigger TEXT NULL;

ALTER TABLE snapshot_draft ADD COLUMN effect_request_id TEXT NULL;

ALTER TABLE snapshot_draft ADD COLUMN effect_lease_until TEXT NULL;

ALTER TABLE snapshot_draft ADD COLUMN server_record INTEGER NULL;

CREATE INDEX IF NOT EXISTS ix_snapshot_draft_effect_pending
    ON snapshot_draft (effect_lease_until, row_id)
    WHERE effect_token IS NOT NULL;
