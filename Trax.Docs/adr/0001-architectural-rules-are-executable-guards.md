---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [testing, platform]
status: accepted
---

# Architectural rules are executable guards, not prose

Trax is ten independent repositories with no shared build and no shared checkout. A
convention that lives only in prose cannot be enforced across that boundary, and this
workspace has already proved the point: every rule the project relies on sat in a single
`CLAUDE.md` at the workspace root, which is not a git repo, so nothing versioned it and no
CI could read it.

A rule that matters therefore gets a test that fails when it is broken, and that test ships
in the repo the rule governs.

## Status

**Accepted.** Recorded after the fact. The `Tests.Meta` projects and the `*.Testing`
packages predate this ADR; what is written down here is the principle they already follow,
so the rest of the corpus has something to hang off.

## Considered options

**Prose plus review.** What the project did until now. It fails in the specific way this
workspace makes worse: a reviewer who has not read the workspace `CLAUDE.md` has no way to
know the rule exists, and a contributor who clones one repo never sees it at all.

**A single guard repo checking all the others.** Rejected because it needs every repo
cloned to say anything, which makes the check unavailable locally and couples eight CI jobs
to one repository's availability.

## Consequences

**A guard is a census, not a detector.** Prefer enumerating every instance and forcing each
to be classified over looking for the mistakes somebody thought of in advance.
`NoSilentRegistrationOrderDependenceTests` is the model: it lists every service-collection
introspection site with a justification, and raising the count to silence it is itself the
violation.

**A guard that cannot fail is worse than no guard**, because it reads as coverage while
enforcing nothing. Every checker reports how many items it inspected, and inspecting zero
is a failure rather than a pass. See `GuardResult.Passed`.

**Rules that span repos are duplicated, not shared.** Nine guard files are byte-identical
copies across up to eight `Tests.Meta` projects. That is the accepted cost of each repo
being able to check itself alone, and it is why those copies cite ADRs in this central
corpus by qualified path rather than by a local path that would have to exist eight times.

## Exemplars

- `GuardRunnerTests` is this ADR applied to itself: it asserts every check reports what it
  inspected, so a checker whose scan silently stopped matching fails instead of passing.
- [Architecture Guards](/docs/reference/architecture-guards) documents the shipped
  `*.Testing` packages and how a consumer adopts them.

**Enforced elsewhere:** each repo's `Tests.Meta` project, plus `HygieneGuards` and
`RepoConventionGuards` shipped from `Trax.Core.Testing`. Those live in the repos they
govern, which this corpus cannot see, so they are recorded here rather than verified here.

Not covered: nothing checks that a rule which *should* have a guard has one. That is the
human step, and no test can detect a decision somebody chose not to record.

## Changelog

- **2026-09-11**: Recorded.
