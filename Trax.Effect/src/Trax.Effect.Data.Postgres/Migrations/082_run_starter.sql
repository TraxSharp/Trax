-- Who started a run, so only they can follow its progress (Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md), and so a user's live invoked runs are
-- counted by the invoked-run cap without reading the instance that queued them, which a draft lifetime can delete.
--
-- starter_hash: the lowercase hex SHA-256 of a tag and the starter's id, never the id itself: "p\0" and the qualified
-- principal id (Trax.Api ADR 0023) for a caller, "u\0" and the user key for an instance's owner. Set on a work queue
-- entry by a GraphQL mutation (its caller) and by a state machine's invoking state (the instance's owner, for a
-- user-owned instance), and copied to the run's metadata at dispatch, as the invoking_ columns are. A run mode mutation
-- writes it on the metadata row directly. A requeue keeps the original run's. A scheduled run, a run the dashboard or
-- the operations namespace starts, and every run from before this migration have none, so no caller can follow them
-- through runProgress.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.work_queue
    ADD COLUMN IF NOT EXISTS starter_hash text NULL;

ALTER TABLE trax.metadata
    ADD COLUMN IF NOT EXISTS starter_hash text NULL;
