-- A search for text inside a log entry's message or category, as the scheduler's log query writes
-- it: lower(message) LIKE '%term%' ESCAPE '\'. With no index the database reads every row it is
-- asked to filter, so a term almost no entry carries, or a count over any text filter, reads the
-- whole table (about 0.4 s at three million rows). A trigram index over the same lowered expression
-- answers those from the index: at three million rows a rare term takes under a millisecond. A term
-- most rows carry still reads by id, as it did, and a term shorter than three characters has no
-- trigram to look up, so it still reads the table.
--
-- pg_trgm ships with Postgres's contrib modules, in every mainstream distribution and managed
-- service, and is a trusted extension from Postgres 13: a role with CREATE on the database can
-- install it without being a superuser. Where the migration's role cannot, install it once as a
-- role that can (CREATE EXTENSION pg_trgm) and this script finds it there. The extension goes into
-- the first schema on the session's search path, normally public, and the operator class is named
-- through that same path, so an existing pg_trgm installed in a schema that is not on the path
-- needs that schema added to the connection string's Search Path.
--
-- Built CONCURRENTLY so log writes carry on while it builds (effect/0014). On a large log table the
-- build is the cost of this upgrade: about 23 s for the message index and 11 s for the category
-- index at three million short entries, longer with longer messages, and it reads the whole table
-- twice. The indexes take about 40% and 20% of the table's size, and every log write after this
-- updates both.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_log_message_trgm
    ON trax.log USING gin (lower(message) gin_trgm_ops);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_log_category_trgm
    ON trax.log USING gin (lower(category) gin_trgm_ops);
