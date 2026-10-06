-- See the Postgres migration of the same name.
ALTER TABLE metadata ADD COLUMN replay_abandoned INTEGER NOT NULL DEFAULT 0;
