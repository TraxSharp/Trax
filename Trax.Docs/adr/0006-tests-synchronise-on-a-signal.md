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
to elapse ("no job was reclaimed within its visibility timeout"). Both are declared with a
marker comment on the same line as the delay or in the three lines above it (`determinism:`, `allowed-delay:`,
`measuring-interval:`, `negative-wait:`), which is what the guard looks for. The marker is
the mechanism, not an allowlist of files.

**A flaky test is never retried.** Retries are the same mistake one layer up.

## Exemplars

**Enforced elsewhere:** `NoFixedTaskDelayTests` in the eight code repos' `Tests.Meta`
projects, and `HygieneGuards.NoFixedDelays` shipped from `Trax.Core.Testing`.

Not covered, and the gap is larger than it looks:

- Seven of the eight repos carry a `BaselineOffenders` dictionary grandfathering the delays
  that already existed, with a per-file count, and fail only when a file exceeds its baseline.
  One Trax.Mediator file is baselined at 13. Nothing fails when a file drops below its
  baseline or disappears, so the numbers only ratchet by hand. Trax.Core has no baseline and
  runs the strict form.
- The guard finds `Task.Delay` and `Thread.Sleep` in test code. It cannot see a fixed sleep
  reached through a helper, a poll whose timeout is too short to be safe, or an unasserted
  polling result.

## Changelog

- **2026-09-11**: Corrected: seven of eight repos carry a baseline, the guard also matches Thread.Sleep, and the marker window looks upward only.
- **2026-09-11**: Corrected the mechanism: legitimate delays are declared with marker
  comments, not an exceptions list, and recorded the BaselineOffenders grandfathering
  the per-repo guards carry.
- **2026-09-11**: Recorded.
