-- See the Postgres migration of the same name.
ALTER TABLE decision ADD COLUMN state_hash TEXT;
