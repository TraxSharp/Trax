-- See the Postgres migration of the same name.
ALTER TABLE junction_run ADD COLUMN name_withheld INTEGER NOT NULL DEFAULT 0;
ALTER TABLE junction_run ADD COLUMN track_position INTEGER;
