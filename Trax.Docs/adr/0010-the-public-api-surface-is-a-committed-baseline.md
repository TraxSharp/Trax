---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli]
areas: [packaging, testing]
status: accepted
---

# The public API surface is a committed baseline

Each publishing repo generates its assemblies' public API to text and compares it against a
file checked into `tests/<Repo>.Tests.Meta/PublicApi/`. A change to anything public fails
the build until the baseline is updated in the same commit.

The first run writes the baseline into the source tree and fails with a message saying to
review and commit it, so adopting it for a new assembly is one deliberate step rather than a
silent accept.

## Status

**Accepted.**

## Considered options

**Nothing, and review the diff.** What most libraries do. It fails on exactly the change this
exists to catch: a public member added or removed by accident, in a file whose diff looks
like an implementation change. Nobody reads a `git diff` asking "did this alter the published
surface", and by the time anyone finds out the package has shipped.

**A semantic-versioning analyzer.** Closer to the real question, since what matters is
whether a change is breaking. Rejected because it needs the previous published version to
compare against, which makes every local build depend on a feed, and because it answers a
narrower question: a baseline diff shows additions too, and an addition is a commitment.

## Consequences

**Updating the baseline is part of the change, not a follow-up.** The regenerated file lands
in the same commit as the code, which is what puts the surface change in front of a reviewer
in a readable form.

**The diff is the review artefact.** A one-line baseline change reads as an addition; a
forty-line one reads as a refactor that got away from someone. That signal is the whole
point, and it is lost if baselines are regenerated in bulk.

**Trax.Samples has no baseline and should not.** It publishes no packages, so there is no
surface to commit to.

## Exemplars

**Enforced elsewhere:** `PublicApiSurfaceTests` in the `Tests.Meta` project of all seven
publishing repos, using `PublicApiGenerator` with assembly attributes excluded and line
endings normalised so the comparison is stable across platforms.

Not covered:

- Nothing checks that the baseline is **current** for an assembly nobody listed. The guard
  iterates an explicit `Assemblies()` list per repo, so a new public assembly added to a repo
  and not added to that list has no baseline and is never compared.
- The guard reports *that* the surface changed, not whether the change is breaking. That
  judgement is the reviewer's, and `BREAKING CHANGE:` in a commit footer is irreversible on
  NuGet.

## Changelog

- **2026-09-11**: Recorded. The census asked which decision `PublicApiSurfaceTests` enforces,
  and there was no answer.
