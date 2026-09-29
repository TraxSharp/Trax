-- See the Postgres migration of the same name.
ALTER TABLE snapshot_draft ADD COLUMN last_request_trigger TEXT NULL;

ALTER TABLE snapshot_draft ADD COLUMN last_request_from_state TEXT NULL;
