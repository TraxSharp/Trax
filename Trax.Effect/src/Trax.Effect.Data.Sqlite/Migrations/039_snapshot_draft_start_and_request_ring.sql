-- See the Postgres migration of the same name: start_request_id and start_fingerprint, the request id
-- and input hash of the start that created the row, unique per owner kind, user and machine; and
-- recent_requests, the row's last sixteen client requests with the result each was answered with, as
-- JSON text.
ALTER TABLE snapshot_draft ADD COLUMN start_request_id TEXT NULL;

ALTER TABLE snapshot_draft ADD COLUMN start_fingerprint TEXT NULL;

ALTER TABLE snapshot_draft ADD COLUMN recent_requests TEXT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_snapshot_draft_start_request
    ON snapshot_draft (owner_kind, user_key, machine, start_request_id)
    WHERE start_request_id IS NOT NULL;
