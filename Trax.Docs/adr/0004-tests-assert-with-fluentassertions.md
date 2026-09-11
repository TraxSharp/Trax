---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [testing]
status: accepted
---

# Tests assert with FluentAssertions

Every assertion uses FluentAssertions. `Assert.That`, `Assert.AreEqual` and the rest of the
legacy NUnit forms are not used, and the guard bans twelve of them by name.

## Status

**Accepted.**

## Why this is written down

Not for style. FluentAssertions carries a `because` argument, and that argument is where a
failing test explains the rule it was protecting instead of only reporting that two values
differed. The habit this project depends on, a guard whose failure message teaches the
reader what to do, is not available in the legacy forms.

`offenders.Should().BeEmpty(explanation)` prints the explanation and the offender list
together. `Assert.IsEmpty(offenders)` prints neither.

## Consequences

**A guard's failure message is part of its contract.** Several of them repeat the
authority they enforce in the message rather than only in the docstring, because that is
what the person who just tripped the guard actually reads.

**The ban is on the legacy shapes, not on NUnit.** `[Test]`, `[TestFixture]`,
`[TestCase]` and `Assert.Ignore` at runtime are all normal, and `Assert.Pass` is fine.

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

- **2026-09-11**: Demoted ReasonsTests from an enforcement claim to an example, and
  recorded that Trax.Docs has no Tests.Meta project to hold the guard.
- **2026-09-11**: Recorded.
