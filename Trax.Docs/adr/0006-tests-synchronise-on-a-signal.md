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

**Enforced elsewhere:** `NoFixedTaskDelayTests` and `NoUnsynchronisedAssertAfterStartTests` in
the eight code repos' `Tests.Meta` projects, and `HygieneGuards.NoFixedDelays` and
`HygieneGuards.NoUnsynchronisedAssertAfterStart` shipped from `Trax.Core.Testing`.

The second guard covers the race with no delay in it. `BackgroundService.StartAsync` hands
`ExecuteAsync` to the thread pool, and a job started with `Task.Run` returns its handle before it
runs, so a substitute checked straight after the start races the work it is checking. After a
`StartAsync` call, a `Received` or `DidNotReceive` in the same method must follow something that
waits for the started work. A start that finishes its work before returning says so with a
`synchronous-start:` comment, the same mechanism as the delay markers.

Seven of the eight repos carry a `BaselineOffenders` dictionary grandfathering the delays that
already existed, with a per-file count. The count is exact: a file with more delays than its
baseline fails, and so does a file with fewer, until its entry is lowered or removed, so a file
that was cleaned up cannot quietly take its delays back.

Not covered:

- The delay guard finds `Task.Delay` and `Thread.Sleep` in test code. It cannot see a fixed
  sleep reached through a helper, a poll whose timeout is too short to be safe, or an unasserted
  polling result.
- The start guard sees a `StartAsync` followed by a substitute check in one method. It cannot
  see a start inside a helper the test calls, a check made through a helper, a state or count
  assertion rather than a substitute check, or a subscription used before the server has
  registered it (graphql-transport-ws sends no acknowledgement of `subscribe`).

## Changelog

- **2026-10-14**: The start guard added; baselines made exact.
- **2026-09-11**: Recorded.
