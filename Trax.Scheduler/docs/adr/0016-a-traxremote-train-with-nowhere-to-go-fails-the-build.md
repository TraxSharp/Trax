---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# A `[TraxRemote]` train with nowhere to go fails the build

A train marked `[TraxRemote]` is routed to the first remote submitter the scheduler has
(`UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers`). When a scheduler has none, the
build now fails and names each such train, instead of running it on the scheduler host's own
workers.

## Status

**Accepted.**

## Why this is written down

Because the attribute is how a consumer says where a train must run, often for isolation (a
train that needs other credentials, other limits or another network), and the old behaviour
quietly ran it in the one place it was marked to leave. The docs called it "silently ignored".

Two answers were weighed. **Warn and run locally**: keeps every configuration building, and
leaves the isolation to whoever reads startup logs. **Refuse to build** (this ADR): a marked
train that cannot be routed is a configuration error, found at the first start of the host.

There is no opt-out. A host that turns remote workers on and off by environment and keeps the
attribute will not build where they are off; an escape hatch would be a new builder method,
and a change to this ADR.

## Exemplars

- `TraxRemoteRoutingBuildTests` pins the refused build, naming the train, and a build that
  succeeds once a routed submitter exists.
- [Use remote workers](/docs/sdk-reference/scheduler-api/use-remote-workers) is the rule this
  produces.

Not covered: `OverrideSubmitter` replaces the default submitter without routing, so a host using
it with a marked train is refused too; nothing checks that a routed submitter actually reaches a
runner that registers the train.

## Changelog

- **2026-09-30**: Recorded.
