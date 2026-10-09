-- See the Postgres migration of the same name: the invoking state an instance was stranded in, when a
-- run it invoked ended and not even its failure could be applied, or null.
ALTER TABLE snapshot_draft ADD COLUMN invoke_stranded_state TEXT NULL;
