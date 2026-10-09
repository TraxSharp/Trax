-- See the Postgres migration of the same name. SQLite cannot drop a primary key or make a column
-- nullable in place, so the table is rebuilt: its columns in the same order, then row_id (the new
-- primary key), owner_kind, invoke_token and invoke_stranded_state, with every row copied across as a
-- user's.
--
-- owner_kind is the integer of SnapshotOwnerKind: 0 is a user, 1 is the system.
CREATE TABLE snapshot_draft_owned (
    id                      TEXT NOT NULL,
    user_key                TEXT NULL,
    machine                 TEXT NOT NULL,
    version                 INTEGER NOT NULL,
    state                   TEXT NOT NULL,
    context                 TEXT NOT NULL DEFAULT '{}',
    concurrency_token       TEXT NOT NULL,
    last_request_id         TEXT,
    updated_at              TEXT NOT NULL,
    last_request_trigger    TEXT NULL,
    last_request_from_state TEXT NULL,
    row_id                  INTEGER NOT NULL CONSTRAINT pk_snapshot_draft PRIMARY KEY AUTOINCREMENT,
    owner_kind              INTEGER NOT NULL DEFAULT 0,
    invoke_token            TEXT NULL,
    invoke_stranded_state   TEXT NULL,
    CONSTRAINT ck_snapshot_draft_owner CHECK (
        (owner_kind = 0 AND user_key IS NOT NULL)
        OR (owner_kind = 1 AND user_key IS NULL)
    )
);

INSERT INTO snapshot_draft_owned (
    id, user_key, machine, version, state, context, concurrency_token, last_request_id, updated_at,
    last_request_trigger, last_request_from_state, owner_kind
)
SELECT
    id, user_key, machine, version, state, context, concurrency_token, last_request_id, updated_at,
    last_request_trigger, last_request_from_state, 0
FROM snapshot_draft;

DROP TABLE snapshot_draft;

ALTER TABLE snapshot_draft_owned RENAME TO snapshot_draft;

CREATE UNIQUE INDEX IF NOT EXISTS ux_snapshot_draft_user_machine_id
    ON snapshot_draft (user_key, machine, id)
    WHERE owner_kind = 0;

CREATE UNIQUE INDEX IF NOT EXISTS ux_snapshot_draft_system_machine_id
    ON snapshot_draft (machine, id)
    WHERE owner_kind = 1;

CREATE UNIQUE INDEX IF NOT EXISTS ux_snapshot_draft_invoke_token
    ON snapshot_draft (invoke_token)
    WHERE invoke_token IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_snapshot_draft_machine_state
    ON snapshot_draft (machine, state);
