-- See the Postgres migration of the same name: an invoked run's output as its machine's OnDone edges
-- read it, written in the run's terminal write, or the mark that it was too large to store.
--
-- SQLite has no notification channel, so there is no trigger: the run's own host applies the outcome
-- through its lifecycle hook, and the reconciler's sweep covers every other case.
ALTER TABLE metadata ADD COLUMN invoke_output TEXT NULL;

ALTER TABLE metadata ADD COLUMN invoke_output_oversize INTEGER NOT NULL DEFAULT 0;
