---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [testing]
status: accepted
---

# A skipped test is a runtime decision, never an attribute

`[Ignore]` is not used. A test that cannot run in the current environment calls
`Assert.Ignore("...")` after an explicit reachability check, so the skip and its reason
appear in the run output.

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

**The guard carries an exceptions list, and that list is itself checked.** Trax.Samples
adds `KnownExceptions_AreNotStale` so an entry cannot outlive the file it exempts.

## Exemplars

**Enforced elsewhere:** `NoIgnoreAttributeTests` in each repo's `Tests.Meta` project, and
`HygieneGuards.NoIgnoreAttribute` shipped from `Trax.Core.Testing`. Trax.Samples carries
the extra staleness check on the exceptions list.

Not covered: nothing distinguishes a well-reasoned `Assert.Ignore` from a lazy one. A
runtime skip with a vague message passes every guard, and only review catches it.

## Changelog

- **2026-09-11**: Recorded.
