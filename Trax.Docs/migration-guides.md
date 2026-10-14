---
title: Upgrading
description: "Upgrade guides for Trax releases that need more than a package bump, newest first, and the database procedures that go with some migrations."
type: landing
area: upgrading
section: How-to guides
nav_order: 1
---

# Upgrading

Most Trax upgrades are a package bump: every Trax package releases at one version, and
`UsePostgres` and `UseSqlite` apply the schema migrations at startup. The guides below cover the
releases that need more, newest first. Each names the release it applies from; before Trax 1.61.0
each package had its own version, so older guides name the package too.

## Guides

| Guide | Applies from |
|---|---|
| [Regenerate twins after the trax machine generate defaults changed](/docs/migration-guides/trax-machine-generate-defaults) | Trax 1.63.1 |
| [Move off the generic state-machine mutations](/docs/migration-guides/generic-state-machine-mutations) | Trax 1.63.1 |
| [Upgrade schedulers from Trax.Effect 1.57.2 or earlier](/docs/migration-guides/scheduler-leader-lock) | Trax.Effect 1.57.3 |
| [Migrate to qualified principal ids](/docs/migration-guides/qualified-principal-ids) | Trax.Api 1.44.0 |
| [Adapt to the enqueue and outcome changes](/docs/migration-guides/enqueue-and-outcome-changes) | Trax.Core 1.7.2, Trax.Effect 1.54.2, Trax.Mediator 1.22.1 |
| [Move trains off RunInternal and Activate](/docs/migration-guides/runinternal-and-activate) | Trax.Core 1.7.2, Trax.Effect 1.54.2 |
| [Upgrade to HotChocolate 16](/docs/migration-guides/hotchocolate-16) | Trax.Api 1.41.0 |
| [Migrate from Hangfire](/docs/migration-guides/migrate-from-hangfire) | Trax.Scheduler 1.0.0 |

## Database procedures

[Schema migrations](/docs/reference/schema-migrations) lists every migration that changes
something visible. Three of them can need a hand:

- [Repair a partial migration 014](/docs/migration-guides/repair-partial-migration-014)
- [Install pg_trgm before migration 063](/docs/migration-guides/install-pg-trgm)
- [Turn off state-machine change notices](/docs/migration-guides/turn-off-snapshot-change-notices) (080)
