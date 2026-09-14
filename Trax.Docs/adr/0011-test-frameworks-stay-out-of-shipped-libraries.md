---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [testing, packaging]
status: accepted
---

# Test frameworks stay out of shipped libraries, except where fixtures are the product

A project under `src/` does not reference NUnit, xUnit, MSTest or the test SDK. The
exception is the `Trax.*.Testing` packages, whose product is architecture-guard fixtures: a
consumer references one, subclasses the fixture and inherits its `[Test]` methods, so the
attributes have to be in the shipped assembly.

## Status

**Accepted.**

## Why this is written down

Because the exception looks like the rule being broken. Four shipped libraries carry NUnit
and eighteen `[Test]` attributes between them, and every one of those is deliberate. Without
this written down, the reasonable reaction to finding `[Test]` in `src/` is to move it into a
test project, which would stop the fixtures shipping and silently remove every consumer's
guards. `Trax.Samples` is the only place in the workspace that consumes them through a real
`PackageReference` today, so the breakage would not show up in this repo's own CI.

The rule that remains is still worth enforcing. Nothing else stopped a test framework
arriving in an ordinary library, where it ships to every consumer as dead weight.

## Considered options

**Ban the reference outright and move the fixtures to `tests/`.** Rejected: test projects are
not packable, so the fixtures would stop being deliverable. The guards would then have to be
copied into each consumer by hand, which is the duplication this packaging exists to avoid.

**Say nothing and rely on review.** Rejected for the reason the whole guard corpus exists:
a convention nobody can run is a convention that drifts.

## Consequences

**The allowlist is the decision, and it is per-package.** Adding a project to
`SanctionedTestingPackages` asserts that shipping fixtures is that package's purpose. The
guard does not check that claim, so it is the one place review still matters.

**A stale exemption is caught.** If a sanctioned package stops referencing a test framework,
the second test fails and the entry must be removed, so the allowlist cannot accumulate
entries that no longer mean anything.

**The `[Test]` methods in `src/` stay invisible to the census.** The census scans a test
project; these fixtures live in `src/` and their checks are credited to no ADR through it.
That is a gap in the census, not in this decision.

## Exemplars

**Enforced elsewhere:** `NoTestFrameworkInSrcTests` in all eight code repos' `Tests.Meta`
projects. It reads every `src/*.csproj`, fails on a test-framework reference outside the
allowlist, and separately fails when an allowlisted project no longer carries one, so the
exemption cannot outlive the thing it exempts. The repos with no `src/` directory skip it at
run time rather than passing vacuously.

Not covered: nothing asserts that a sanctioned package actually exposes fixtures rather than
merely referencing NUnit, and nothing checks the reverse direction, that a package whose
purpose is fixtures has not been left off the list. Both are review's job.

## Changelog

- **2026-09-14**: Recorded.
