---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [testing]
status: accepted
---

# A skipped test is a runtime decision, never an attribute

A test that cannot run in the current environment calls `Assert.Ignore("...")` after an
explicit reachability check, so the skip and its reason appear in the run output. The
`[Ignore]` attribute is not used for that, and the guard rejects new ones.

One remains, with a justification the guard's exceptions list records: a Trax.Samples E2E
test waiting on an unreleased scheduler feature. A suite meant to be run by hand is a
legitimate shape this rule does not cover, and `[Explicit]` on each concrete fixture is the
form that works, as the Trax.Api and Trax.Scheduler stress suites use it: NUnit does not inherit `[Ignore]` or `[Explicit]` from a base class,
so either attribute on an abstract fixture skips nothing.

## Status

**Accepted.**

## Why this is written down

Because the two forms look equivalent and are not. `[Ignore]` almost always means there is
a real bug under the test that nobody wanted to debug, and silencing the signal lets the
bug stay broken indefinitely. The attribute hides at declaration time; a runtime skip shows
up in every run, with a reason attached, and disappears by itself once the dependency it
was waiting on is available.

The honest responses to a failing test are: fix the code, or fix the test's premise and say
why in the same change. Neither of those is an attribute.

## Consequences

**Infrastructure-dependent tests probe first.** A test needing RabbitMQ checks
reachability and calls `Assert.Ignore` with the endpoint in the message, rather than being
disabled for everyone including the environments where it would pass.

**Three of the eight repos carry an exceptions list, and in one that list is itself checked.**
Trax.Samples adds `KnownExceptions_AreNotStale` so an entry cannot outlive the file it
exempts. The other seven copies have no such check, so a stale exemption there is invisible.

## Exemplars

**Enforced elsewhere:** `NoIgnoreAttributeTests` in the eight code repos' `Tests.Meta`
projects, and `HygieneGuards.NoIgnoreAttribute` shipped from `Trax.Core.Testing`.
Trax.Docs is bound by this decision but has no `Tests.Meta` project and so no copy.

Not covered:

- Nothing distinguishes a well-reasoned `Assert.Ignore` from a lazy one. A runtime skip
  with a vague message passes every guard, and only review catches it.
- The per-repo regex misses `[Test, Ignore("x")]`, which the shipped
  `HygieneGuards` form does catch. The guards actually running are weaker than the one
  that ships.

## Changelog

- **2026-09-27**: Trax.Scheduler's stress suite made the same move, to `[Explicit]` on each
  concrete fixture. One exception remains.
- **2026-09-27**: Trax.Api's stress suite moved from `[Ignore]` on its abstract base, which
  NUnit does not inherit and so skipped nothing, to `[Explicit]` on the concrete fixture.
  Two exceptions remain, not three.
- **2026-09-11**: Recorded.
