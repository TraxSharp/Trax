-- See the Postgres migration of the same name.
CREATE TABLE IF NOT EXISTS runner_nonce (
    nonce TEXT PRIMARY KEY,
    expires_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_runner_nonce_expires_at
    ON runner_nonce (expires_at);
