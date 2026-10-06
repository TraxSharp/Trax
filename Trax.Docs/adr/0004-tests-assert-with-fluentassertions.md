---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [testing]
status: accepted
---

# Tests assert with AwesomeAssertions

Every assertion uses AwesomeAssertions, the Apache-2.0 fork of FluentAssertions 7 with the same
`.Should()` API. `Assert.That`, `Assert.AreEqual` and the rest of the
legacy NUnit forms are not used, and the guard bans twelve of them by name.

## Status

**Accepted.**

## Why this is written down

Not for style. A fluent assertion carries a `because` argument, and that argument is where a
failing test explains the rule it was protecting instead of only reporting that two values
differed. The habit this project depends on, a guard whose failure message teaches the
reader what to do, is not available in the legacy forms.

`offenders.Should().BeEmpty(explanation)` prints the explanation and the offender list
together. `Assert.IsEmpty(offenders)` prints neither.

**Why AwesomeAssertions rather than FluentAssertions.** This decision named FluentAssertions until
2026-10-03. FluentAssertions 8 moved to the Xceed Community License, which charges for commercial
use; Trax is MIT and is worked on in commercial settings, and the project templates would have
handed the same licensing question to everyone who scaffolds from them. AwesomeAssertions keeps the
API and the `because` argument this decision depends on, under Apache-2.0. The switch was a
namespace and package rename: Trax.Core's 715 tests needed no assertion rewritten. Pinning
FluentAssertions 7 (still Apache-2.0) was rejected because it only receives critical fixes, and NUnit's
constraint model or Shouldly were rejected because they would have meant rewriting every assertion.

## Consequences

**A guard's failure message is part of its contract.** Several of them repeat the
authority they enforce in the message rather than only in the docstring, because that is
what the person who just tripped the guard actually reads.

**The ban is on the legacy shapes, not on NUnit.** `[Test]`, `[TestFixture]`,
`[TestCase]` and `Assert.Ignore` at runtime are all normal, and `Assert.Pass` is fine.

**The shipped guard fixtures are the one exception, and it is deliberate.** The abstract
`*GuardFixture` classes in `Trax.Core.Testing`, `Trax.Effect.Data.Testing`,
`Trax.Api.GraphQL.Testing` and `Trax.Mediator.Testing` use `Assert.That`, because they
depend on NUnit and forcing an assertion library on every consumer to inherit a fixture would be
a worse trade. They live in `src/` and the guard scans `tests/`, so it cannot see them.

## Exemplars

**Enforced elsewhere:** `NoLegacyAssertTests` in the eight code repos' `Tests.Meta`
projects, and `HygieneGuards.NoLegacyAsserts` shipped from `Trax.Core.Testing` for
consumers. Trax.Docs is bound by this decision but has no `Tests.Meta` project and so no
copy; its assertions are held to it by review alone.

`ReasonsTests.cs` in this repo is worth reading as an example of the `because` argument
carrying the reason, but it is a unit test rather than a guard and enforces nothing.

Not covered: nothing checks that a `because` argument is *present*, only that the assertion
style allows one. A bare `.Should().BeEmpty()` passes the guard and teaches nobody, and
most assertions in this repo's own test project are exactly that.

## Changelog

- **2026-10-03**: The library is AwesomeAssertions, not FluentAssertions, for the licence reason above.
  The decision (a fluent assertion with a `because` argument, no legacy NUnit forms) is unchanged, so
  it is amended rather than superseded, and the file keeps its original name so every guard's
  citation still resolves.
- **2026-09-11**: Recorded.
