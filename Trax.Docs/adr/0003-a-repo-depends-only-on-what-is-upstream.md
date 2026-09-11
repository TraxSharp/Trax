---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [packaging, platform]
status: accepted
---

# A repo depends only on what is upstream of it

The repos are a **total order**, not a tree, and a repo may reference only the ones before
it:

`Trax.Core`, `Trax.Effect`, `Trax.Mediator`, `Trax.Scheduler`, `Trax.Api`, `Trax.Dashboard`

`Trax.Cli` sits after `Trax.Scheduler`, and `Trax.Samples` last of all, referencing
everything. So `Trax.Api` referencing `Trax.Scheduler` is upstream and allowed; the reverse
is not. There are no siblings in this model, which is what makes the rule checkable: every
pair of repos has a direction.

## Status

**Accepted.** Recorded after the fact: the chain predates the ADRs, and no moment weighed
an alternative.

## Why this is written down

Because the constraint is invisible at the point where it would be violated. Nothing in
`Trax.Scheduler` announces that `Trax.Api` is downstream of it, and adding a
`PackageReference` is one line that compiles fine locally against a packed `1.99.99` feed.
The cycle only shows up later, as a restore that cannot be satisfied or a release order
that cannot be produced.

It is also the reason several other decisions look the way they do. Cross-schema joins and
GraphQL edges live in one project rather than being spread across the contexts they touch,
because the alternative needs edges pointing sideways.

## Consequences

**A capability in the wrong repo is a move, not a reference.** When `Trax.Scheduler` needs
something that lives in `Trax.Api`, the answer is to push it down into `Trax.Effect` or
`Trax.Core`, not to reach across.

**Reuse below you is duplication.** A repo that needs a helper living downstream either
pushes it upstream or keeps its own copy. The duplicated guard files across the
`Tests.Meta` projects are this rule in action.

## Exemplars

- [Getting Started](/docs/getting-started) shows the layering a consumer sees, which is the
  same chain read from the outside.

**Enforced elsewhere:** `DependencyDirectionTests` in each repo's `Tests.Meta` project. It
identifies the repo from the root `.slnx` and allows only that repo's own family plus the
upstreams named in its allow-map, which is where the order above is actually written down.

Not covered, and worth knowing:

- The guard reads `PackageReference` entries, so a dependency taken by copying source
  across repos is invisible to it.
- It only recognises the eight `Trax.*` families it lists. `Trax.Samples` references
  `Trax.Runner.Lambda`, published out of Trax.Scheduler, and the guard skips it because the
  name is not in that list.

## Changelog

- **2026-09-11**: Corrected the chain. The repos are a total order, not Core and Effect
  above an undifferentiated rest, and what the ADR called siblings the guard has always
  allowed.
- **2026-09-11**: Recorded.
