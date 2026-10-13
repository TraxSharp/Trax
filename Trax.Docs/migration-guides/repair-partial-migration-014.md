---
title: Repair a partial migration 014
description: "How to recover a Postgres database whose 014_manifest_group migration stopped partway, so the next start can apply it or skip it."
type: how-to
area: data
nav_order: 10
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations/014_manifest_group.sql
---

# Repair a partial migration 014

`014_manifest_group.sql` cannot run twice. If it stopped partway (a killed process, a failed
statement), the next start runs it again from the top and fails on `CREATE TABLE trax.manifest_group`,
because the table is already there. What it does is described in
[Schema migrations](/docs/reference/schema-migrations#manifest-groups-014).

Stop every host on the database before you start, and take a backup.

## 1. Confirm 014 is not recorded

```sql
SELECT scriptname, applied FROM trax.migrations ORDER BY applied DESC LIMIT 5;
```

If a row ends in `014_manifest_group.sql`, the migration completed and this guide does not apply:
the failure is elsewhere.

## 2. Find how far it got

```sql
SELECT column_name FROM information_schema.columns
WHERE table_schema = 'trax' AND table_name = 'manifest'
  AND column_name IN ('group_id', 'manifest_group_id');
```

| Result | The script |
|---|---|
| `group_id` only | stopped after creating `trax.manifest_group`, or while seeding it |
| both | stopped while filling or constraining `manifest_group_id`, or before dropping `group_id` |
| `manifest_group_id` only | ran every statement; only the journal row is missing |

## 3a. While `group_id` still exists: undo it

Remove what the script created, then start a host. 014 runs again from the top, against the
manifests as they were.

```sql
ALTER TABLE trax.manifest DROP CONSTRAINT IF EXISTS fk_manifest_manifest_group;
DROP INDEX IF EXISTS trax.ix_manifest_manifest_group_id;
ALTER TABLE trax.manifest DROP COLUMN IF EXISTS manifest_group_id;
DROP TABLE IF EXISTS trax.manifest_group;
```

## 3b. When `group_id` is gone: record it

Every statement ran, so add the journal row DbUp did not write. Copy the `scriptname` of the 013 row
and change its file name to `014_manifest_group.sql`:

```sql
INSERT INTO trax.migrations (scriptname, applied)
SELECT replace(scriptname, '013_priority.sql', '014_manifest_group.sql'), now()
FROM trax.migrations
WHERE scriptname LIKE '%013_priority.sql';
```

Then start a host. The migrator skips 014 and applies the scripts after it.

## Related

- [Schema migrations](/docs/reference/schema-migrations): every migration that changes something visible.
- [Writing migrations](/docs/contributing/writing-migrations#every-postgres-script-can-run-again): why later scripts can run again and 014 cannot.
