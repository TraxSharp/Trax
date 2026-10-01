---
authors: [Theauxm]
areas: [scheduling]
status: accepted
---

# A startup prune deletes only its own application's manifests

`PruneOrphanedManifests` (on by default) deletes, at each start, the manifests a host no longer
declares. Every manifest the scheduler writes now records the application that declared it in
`manifest.owner` (the host environment's `ApplicationName`, or the entry assembly's name when
there is no `IHostEnvironment`), and the prune considers only manifests carrying this
application's name. A manifest another application owns, or one with no owner, is never pruned,
and a host whose name cannot be found prunes nothing.

## Status

**Accepted.**

## Why this is written down

Because the prune compared the whole table with one host's declarations, so the cost of a
mistake was everything: two applications scheduling against one database deleted each other's
manifests, with their queued work, dead letters and finished runs, at every start.

Two answers were weighed. **Make pruning opt-in**: safe, but every application that renames or
drops a schedule keeps its stale manifest running until someone notices. **Scope it by owner**
(this ADR): the prune keeps doing its job inside the application that owns the manifests, and
cannot reach past it.

The scoping fails closed. A manifest written before the owner column existed has no owner, and
the prune cannot tell whose it is, so it keeps it. That leaves an application's own pre-upgrade
manifests that it no longer declares to an operator; re-declaring a manifest stamps it, so only
schedules removed from code before the upgrade are affected. A host whose name resolves to
nothing skips the prune with a warning rather than guessing.

## Consequences

**The owner is the application, not the version.** An old and a new version of one application
share a name, so during a rolling deploy the old one can still prune a manifest only the new
one declares. The empty-set guard (a host declaring nothing prunes nothing) and the guard for a
manifest with a Pending or InProgress run still apply.

**A named batch's `PrunePrefix` prune is not owner-scoped.** It deletes within its own group and
prefix, which the build already keeps from overlapping another batch or schedule.

## Exemplars

- `ManifestOwnerTests` pins the owner stamped on seed, a prune that keeps another application's
  and an unowned manifest, and a host with no name pruning nothing.
- [Orphan manifest cleanup](/docs/scheduler/orphan-manifest-cleanup) is the rule this produces.

Not covered: nothing stops two different applications from configuring the same
`ApplicationName`; they then prune each other as before.

## Changelog

- **2026-09-30**: Recorded.
