-- A search of runs by what they failed with, as the GraphQL executions query writes it:
-- lower(failure_reason) LIKE '%term%' ESCAPE '\', and failure_junction = 'Name'. Nothing served
-- either, so a term or junction few runs carry, and the exact count of any match, read the whole
-- run table. The trigram index over the lowered reason answers a rare or absent term from the
-- index, as 063 does for the log's text; a term most failed runs carry still reads by id, and a
-- term shorter than three characters has no trigram to look up, so it still reads the table. The
-- junction index holds only runs that failed, in id order under each junction, so a page of one
-- junction's failures, newest first, reads just that page.
--
-- pg_trgm is installed by 063, and its header says what to do when the migrating role cannot
-- create the extension; the CREATE EXTENSION here is a no-op after it.
--
-- Built CONCURRENTLY so runs carry on being written while they build (effect/0014). On a large
-- run table the build is the cost of this upgrade: it reads the whole table once per index. At
-- three million runs, two in nine of them failed with a reason of about fifty characters, the
-- trigram index took about 7 s to build and 50 MB (8% of the table), the junction index under a
-- second and 30 MB; the trigram index grows with how many runs failed and how long their reasons
-- are. A rare term then takes a few milliseconds. Even with the index, counting a term most
-- failed runs carry takes about 0.3 s, and a newest-first page of a term only old runs carry
-- about 0.6 s when read in id order, so the executions query caps that count and reads such a
-- page in two steps. Every run
-- written afterwards adds to the trigram index (a single null entry when it has no reason); only
-- a run that failed in a junction adds to the junction index.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_metadata_failure_reason_trgm
    ON trax.metadata USING gin (lower(failure_reason) gin_trgm_ops);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_metadata_failure_junction
    ON trax.metadata (failure_junction, id)
    WHERE failure_junction IS NOT NULL;
