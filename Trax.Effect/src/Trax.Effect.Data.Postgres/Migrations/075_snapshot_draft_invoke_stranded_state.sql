-- Marks a state-machine instance stranded in an invoking state (Trax.Effect.StateMachine.Persistence,
-- Trax.Docs/adr/0046). Mirrors SnapshotDraft.InvokeStrandedState.
--
-- invoke_stranded_state: the invoking state an instance was stranded in, when a run it invoked ended
-- and not even its failure could be applied. The one conditional update that clears the token writes
-- it, so an instance left in an invoking state with no live run on purpose is told apart from one that
-- lost its token by mistake. A write that gives the row a token, or moves it to another state, clears
-- it. Server-only, null otherwise. Added without a default, so the table is not rewritten.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS invoke_stranded_state text NULL;
