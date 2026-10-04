-- See the Postgres migration of the same name.
ALTER TABLE junction_run ADD COLUMN attempt INTEGER;
