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

- `ReasonsTests` in this repo shows the `because` argument carrying the reason rather than
  the test name carrying it alone.

**Enforced elsewhere:** `NoLegacyAssertTests` in each repo's `Tests.Meta` project, and
`HygieneGuards.NoLegacyAsserts` shipped from `Trax.Core.Testing` for consumers.

Not covered: nothing checks that a `because` argument is *present*, only that the assertion
style allows one. A bare `.Should().BeEmpty()` passes the guard and teaches nobody.

## Changelog

- **2026-09-11**: Recorded.
