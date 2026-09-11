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
enforcing nothing. The ADR guard therefore reports how many items each check inspected and
treats zero as a failure unless the check declares that having nothing to check is
legitimate (`Trax.Adr.Guard.GuardResult.Passed`). **The shipped `Trax.Core.Testing`
`GuardResult` does not do this**: its `Passed` ignores `Inspected`, and no guard in any
`Tests.Meta` project reads the count. Carrying that idea into the shipped packages is
outstanding work, not a described state.

**Rules that span repos are duplicated, not shared.** Eight guard file names appear in all
eight `Tests.Meta` projects and eleven are duplicated across two or more. The copies are
near-identical rather than identical: each carries its own namespace, and some have diverged
further (Trax.Samples adds `KnownExceptions_AreNotStale` to its `NoIgnoreAttributeTests`).
That is the accepted cost of each repo checking itself alone.

**A shared copy must cite a path that resolves in every repo holding it.** A citation of
`docs/adr/0003-x.md` would have to exist eight times over; one of `Trax.Docs/adr/0003-x.md`
is the same string everywhere, which is why every shared guard uses that form. Repo-specific
guards cite their own repo's path instead. Between them, 66 guard files across the eight code
repos name the ADR they enforce.

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

- **2026-09-11**: Corrected the citation note again: it still said no shared copy carried
  one, written before the census pass added 66 of them.
- **2026-09-11**: Updated the citation note: repo-specific guards in Trax.Effect and
  Trax.Api now cite their local ADRs, though no shared copy does.
- **2026-09-11**: Corrected three claims an audit found false: the Inspected rule holds
  only for the ADR guard and not for the shipped Trax.Core.Testing GuardResult, the
  duplicated guard files are near-identical rather than byte-identical, and no
  Tests.Meta guard cites an ADR yet.
- **2026-09-11**: Recorded.
