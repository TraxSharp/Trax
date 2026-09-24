-- See the Postgres migration of the same name. SQLite stores the enum as its integer, so the
-- default is FailureClass.Unclassified = 0, not its label.
ALTER TABLE metadata ADD COLUMN failure_class INTEGER NOT NULL DEFAULT 0;
