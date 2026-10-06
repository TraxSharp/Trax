-- See the Postgres migration of the same name. SQLite cannot change a primary key in place, so the table is
-- rebuilt under the wider key with its columns in the same order, and every row is copied across.
CREATE TABLE snapshot_draft_rekeyed (
    id                      TEXT NOT NULL,
    user_key                TEXT NOT NULL,
    machine                 TEXT NOT NULL,
    version                 INTEGER NOT NULL,
    state                   TEXT NOT NULL,
    context                 TEXT NOT NULL DEFAULT '{}',
    concurrency_token       TEXT NOT NULL,
    last_request_id         TEXT,
    updated_at              TEXT NOT NULL,
    last_request_trigger    TEXT NULL,
    last_request_from_state TEXT NULL,
    CONSTRAINT pk_snapshot_draft PRIMARY KEY (user_key, machine, id)
);

INSERT INTO snapshot_draft_rekeyed (
    id, user_key, machine, version, state, context, concurrency_token, last_request_id, updated_at,
    last_request_trigger, last_request_from_state
)
SELECT
    id, user_key, machine, version, state, context, concurrency_token, last_request_id, updated_at,
    last_request_trigger, last_request_from_state
FROM snapshot_draft;

DROP TABLE snapshot_draft;

ALTER TABLE snapshot_draft_rekeyed RENAME TO snapshot_draft;
