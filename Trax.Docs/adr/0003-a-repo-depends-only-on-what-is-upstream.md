---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [packaging, platform]
status: accepted
---

# A repo depends only on what is upstream of it

The dependency chain runs one way: `Trax.Core`, then `Trax.Effect`, then everything else
(`Mediator`, `Scheduler`, `Dashboard`, `Api`, `Samples`). A repo may reference its own
packages and those of a repo above it, never one below it and never a sibling.

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

**Sibling reuse is duplication.** Two parallel repos that need the same helper either get
it from a shared upstream or each keep their own copy. The nine duplicated guard files
across the `Tests.Meta` projects are this rule in action.

## Exemplars

- [Getting Started](/docs/getting-started) shows the layering a consumer sees, which is the
  same chain read from the outside.

**Enforced elsewhere:** `DependencyDirectionTests` in each repo's `Tests.Meta` project. It
detects the repo from the root `.slnx` and allows only that repo's own family plus its
declared upstreams.

Not covered: the guard reads `PackageReference` entries, so it cannot see a dependency
taken by copying source across repos.

## Changelog

- **2026-09-11**: Recorded.
