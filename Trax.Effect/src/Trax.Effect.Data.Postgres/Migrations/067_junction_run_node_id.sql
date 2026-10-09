-- The id of the declared node a step ran for, as ChainGraph draws it, so a run's timeline can be
-- laid over its train's graph. Rows written before this have none.
ALTER TABLE trax.junction_run ADD COLUMN IF NOT EXISTS node_id text;
