---
title: Upgrade schedulers from Trax.Effect 1.57.2 or earlier
description: "Why a Postgres scheduler fleet on Trax.Effect 1.57.2 or earlier must stop every host before the first one starts on a newer version, and how to deploy it."
type: how-to
area: upgrading
parent: Upgrading
nav_order: 2
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Services/SqlDialect/PostgresSqlDialect.cs
---

# Upgrade schedulers from Trax.Effect 1.57.2 or earlier

Applies from: Trax.Effect 1.57.3.

The ManifestManager runs on one host at a time, the one holding a Postgres advisory lock keyed on
`hashtext('trax_manifest_manager')`. Trax.Effect 1.57.2 and earlier put the lock name inside SQL
quotes, so Postgres hashed the text of EF's parameter placeholder instead of the name. From 1.57.3
the name itself is hashed. An old host and a new one therefore take different keys, and both run
the ManifestManager at once for as long as both are up. How the lock works is in
[Concurrency](/docs/scheduler/concurrency).

## What goes wrong in a rolling deploy

Two leaders each run a full cycle (reaping failed jobs, creating dead letters, queueing manifests)
against the same manifests. The one-queued-entry-per-manifest index still refuses a duplicate queue
row, but the rest of each cycle runs twice. SQLite hosts take no leader lock and are not affected.

## Deploy the upgrade

1. Stop every host that runs the scheduler against the database.
2. Deploy the new version.
3. Start the hosts.

Later upgrades roll as usual: every version from 1.57.3 on takes the same key.

## Related

- [Concurrency](/docs/scheduler/concurrency): the leader lock and the other locks the scheduler takes.
- [Schema migrations](/docs/reference/schema-migrations): what else an upgrade applies.
