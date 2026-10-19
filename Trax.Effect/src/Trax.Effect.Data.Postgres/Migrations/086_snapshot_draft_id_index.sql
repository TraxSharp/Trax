-- Finds a state-machine instance by its id alone, across machines and owners, for the operator's lookup of an id
-- of unknown kind (Trax.Api's operations.lookup, through IOperationsService.LookupAsync). The other keys on
-- trax.snapshot_draft lead with the machine or the user, so a GUID with neither read the whole table.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.

-- CONCURRENTLY so drafts keep being saved and advanced while it builds (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_snapshot_draft_id
    ON trax.snapshot_draft (id);
