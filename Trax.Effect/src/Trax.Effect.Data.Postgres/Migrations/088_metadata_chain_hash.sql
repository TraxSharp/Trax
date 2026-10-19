-- The chain a run started under, so a run graph drawn later on a chain that has changed since can say so.
--
-- chain_hash: ChainGraph.Hash of the train's declared chain when the run started, 64 lowercase hex, as the host's
-- startup check read it. Null when the host had not read the chain (it skips the check) and on every run from before
-- this migration; such a run's graph is drawn on the current chain with nothing to compare it to.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
ALTER TABLE trax.metadata
    ADD COLUMN IF NOT EXISTS chain_hash text NULL;
