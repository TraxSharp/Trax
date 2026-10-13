---
title: Install pg_trgm before migration 063
description: "How to install the pg_trgm extension, or build the trigram indexes ahead of time, when the role Trax migrates with cannot run migration 063 itself."
type: how-to
area: data
nav_order: 11
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations/063_log_text_search_trigram.sql
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations/066_metadata_failure_search.sql
---

# Install pg_trgm before migration 063

`063_log_text_search_trigram.sql` starts with `CREATE EXTENSION IF NOT EXISTS pg_trgm`. Since
Postgres 13 `pg_trgm` is a trusted extension, so a role with `CREATE` on the database installs it
without being a superuser. When the role Trax migrates with cannot, the migration fails and the host
does not start. What 063 adds is described in
[Schema migrations](/docs/reference/schema-migrations#log-text-search-063).

## 1. Check whether it is installed

```sql
SELECT e.extname, n.nspname AS schema
FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace
WHERE e.extname = 'pg_trgm';
```

A row means it is installed: go to step 3. No row, and
`SELECT * FROM pg_available_extensions WHERE name = 'pg_trgm';` also returns none, means the server
lacks Postgres's contrib modules; install them through your distribution or managed service first.

## 2. Install it as a role that can

Connect to the Trax database as a role allowed to create the extension, and run:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
```

It goes into the first schema on that session's search path, normally `public`.

## 3. Put its schema on Trax's search path

The migration names the `gin_trgm_ops` operator class through the search path. If the query in step 1
shows a schema that is not on the search path of the role Trax connects with, add it to the
connection string, for example `Search Path=trax,public,extensions`.

## 4. Optionally, build the indexes ahead of the upgrade

On a large `trax.log` the index builds are the cost of this upgrade (about 23 s and 11 s at three
million short entries), and startup waits for them. The migration skips an index that already
exists under its name, so the indexes can be built beforehand, on the running version, as the
role Trax migrates with:

```sql
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_log_message_trgm
    ON trax.log USING gin (lower(message) gin_trgm_ops);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_log_category_trgm
    ON trax.log USING gin (lower(category) gin_trgm_ops);
```

Migration `066_metadata_failure_search.sql` uses the same extension for its trigram index on
`trax.metadata` and needs nothing more once 063 has run. Its two indexes can be built ahead the
same way, from the statements in that script.

## Related

- [Schema migrations](/docs/reference/schema-migrations#failure-search-066): what 066 adds.
- [`LikePattern`](/docs/sdk-reference/configuration/like-pattern): the text filters the indexes serve.
