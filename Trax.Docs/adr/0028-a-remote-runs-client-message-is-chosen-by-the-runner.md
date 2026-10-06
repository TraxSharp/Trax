---
authors: [Theauxm]
repos: [scheduler, api]
areas: [platform, graphql]
status: accepted
---

# A remote run's client-facing message is chosen by the runner

When a run on a remote runner fails, the runner decides what a client of the calling side may
read, and sends it as `RemoteRunResponse.PublicMessage`. It offers only the message of a plain
`TrainException`, which a train author wrote for the caller, and nothing for any other failure.
The calling side rebuilds every remote failure, transport failures included, as a
`RemoteRunException` carrying that message, so a surface that shows errors to clients reads one
property: the message when there is one, and "the train failed" when there is not.

## Status

**Accepted.**

## Considered options

**The API recognises the scheduler's message formats.** What Trax.Api's error filter does until it
picks this up (Api ADR 0014): it masks a `TrainException` whose message starts with one of the
remote executors' prefixes. It couples the API to strings another repo writes, and it already
missed one: the Lambda executor's messages match neither prefix.

**A distinct exception type alone.** The calling side could mark remote failures without a wire
field and let the API mask them all. Rejected because a train author's refusal ("Order 42 is
already closed") would then read the same as a crashed worker once the train runs remotely.

**Decide on the calling side from `ExceptionType`.** Workable, but it moves a rule about what is
safe to show into every reader of the response. The runner holds the real exception, the same
reason [0020](./0020-a-failure-is-classified-where-it-happens-and-carried.md) classifies there.

## Consequences

A runner that predates the field sends none, so its failures reach a client as "the train
failed" even when a train author wrote the message. Runners and callers normally upgrade
together, since both are the same Trax.Scheduler package.

`PublicMessage` is an init property, not a constructor parameter, so the constructor a separately
shipped runner package was built against stays (`scheduler/0001`).

## Exemplars

**Enforced elsewhere:** `RemoteRunPublicMessageTests` in Trax.Scheduler's `Trax.Scheduler.Tests`
pins what the runner offers for each kind of failure, the rebuilt `RemoteRunException`, and the
field on the wire; `HttpRunExecutorTests` and `LambdaRunExecutorTests` beside it pin that a
transport failure carries no message.

Not covered: nothing in this repo checks that Trax.Api reads `PublicMessage`. Until it does, its
error filter's prefix rule (Api ADR 0014) is what masks remote failures.

## Changelog

- **2026-09-27**: Recorded.
