-- See the Postgres migration of the same name.
ALTER TABLE manifest ADD COLUMN replay_decisions_on_retry INTEGER NOT NULL DEFAULT 1;
