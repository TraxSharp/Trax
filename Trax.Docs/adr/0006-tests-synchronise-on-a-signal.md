---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [testing]
status: accepted
---

# Tests synchronise on a signal, never on a fixed delay

A test waiting for asynchronous work polls the condition that means the work finished, with
a generous timeout as a ceiling, and asserts the wait succeeded. It does not sleep for a
duration chosen to be long enough.

## Status

**Accepted.**

## Considered options

**A fixed `Task.Delay` sized generously.** Simpler to write, and the reason it keeps
reappearing. It races CI scheduling: a bound that holds on a developer machine does not
hold under 10x load variance, and the failure arrives months later as a test that fails
"sometimes". That teaches developers to retry rather than investigate, which is how a real
regression slips through.

## Consequences

**Polling is faster, not slower.** A test that waits on the condition finishes as soon as
it is true, where a fixed sleep always pays its full duration.

**A polling helper's return value must be asserted.** `await WaitUntilAsync(...)` that
times out and returns false, unchecked, masks the regression instead of reporting it. The
assertion carries the `because` that says what should have happened.

**Two uses of a fixed delay remain legitimate**: measuring an interval ("these two
timestamps are at least 50 ms apart"), and verifying a negative that requires the duration
to elapse ("no job was reclaimed within its visibility timeout"). Both need a comment
saying which, and both are why the guard has an exceptions list rather than a flat ban.

**A flaky test is never retried.** Retries are the same mistake one layer up.

## Exemplars

**Enforced elsewhere:** `NoFixedTaskDelayTests` in each repo's `Tests.Meta` project, and
`HygieneGuards.NoFixedDelays` shipped from `Trax.Core.Testing`.

Not covered: the guard finds `Task.Delay` in test code. It cannot see a fixed sleep reached
through a helper, nor a poll whose timeout is too short to be safe, nor an unasserted
polling result.

## Changelog

- **2026-09-11**: Recorded.
