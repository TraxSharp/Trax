---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [packaging, platform]
status: accepted
---

# A repo depends only on what is upstream of it

The repos form a **partial order**, and a repo may reference only what is upstream of it:

```
Trax.Core -> Trax.Effect -> Trax.Mediator -> Trax.Scheduler -> Trax.Api -> Trax.Dashboard
                                                           \-> Trax.Cli
```

`Trax.Samples` sits below all seven and references everything. So `Trax.Api` referencing
`Trax.Scheduler` is upstream and allowed; the reverse is not.

It is a partial order rather than a single line because `Trax.Api` and `Trax.Cli` are both
downstream of `Trax.Scheduler` and neither may reference the other, so that pair has no
direction at all. What makes the rule checkable is that each repo's upstream set is written
down, not that every pair is ordered.

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

**Enforced elsewhere:** `DependencyDirectionTests` in each repo's `Tests.Meta` project, the
same file in all eight apart from its namespace line. It identifies the repo from the root
`.slnx` and allows only that repo's own family plus the upstreams named in its allow-map,
which is where the order above is actually written down: one explicit upstream set per repo,
not a position in a line. `Trax.Api` and `Trax.Cli` carry the same four upstreams and neither lists
the other, which is the partial order made checkable.

Not covered:

- The guard reads `PackageReference` entries, so a dependency taken by copying source
  across repos is invisible to it.
- It only recognises the eight `Trax.*` families it lists. `Trax.Samples` references
  `Trax.Runner.Lambda`, published out of Trax.Scheduler, and the guard skips it because the
  name is not in that list.

## Changelog

- **2026-09-11**: Corrected the opening claim that the repos are a total order. The allow-map
  in `DependencyDirectionTests` encodes Api and Cli as parallel branches off Scheduler, and the
  body already said so two paragraphs later.
- **2026-09-11**: Recorded.
