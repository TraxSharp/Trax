---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# The lifecycle feed is lossy and numbers its events

The lifecycle subscriptions (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`,
`onTrainCancelled`, `onTrainStateChanged`) are a live feed, not a log. Under load a subscriber
that falls behind loses events, and it is told: every event carries `sequence`, its position in
the subscription, which counts up by one and skips a number after any loss. A client that sees a
skip refetches what it shows, from `operations.executions` or its own queries.

`LifecycleEventPublisher` numbers each topic's events as it sends them, under one lock per
topic, so the numbers follow the order the transport receives them in. Each subscription
remembers the last number it read, starting from the topic's last number when it subscribed,
and a jump past it means HotChocolate's per-subscriber buffer dropped something in between.

## Status

**Accepted.**

## Considered options

**A bigger topic buffer.** HotChocolate gives each subscriber a bounded buffer per topic (64
events, dropping the oldest when full). Raising it moves the point where events are lost
without removing it, and costs memory per subscriber per topic on every host. Stress runs at
1,000 subscribers and about 500 events a second lost around 5% of events for the slowest
subscribers; any finite buffer has a load like that.

**Never dropping (blocking the publisher).** Backpressure on the publisher would make one slow
socket slow down every train's lifecycle hooks on the host. The feed exists to observe runs; it
must not hold them up.

**Resumable delivery (a replay from the last number).** The pattern of SSE's `Last-Event-ID`
and Kafka offsets. It needs an event store the feed reads from, which Trax already has in the
metadata table and its queries. A client that refetches on a gap gets the same outcome without
a second store.

**A gap of the true size.** The subscription knows how many publishes it missed, but some may
be events of trains it may not see. Reporting the count would tell a broadcast subscriber how
busy those trains are, so a loss is always reported as one skipped number.

## Consequences

**A loss of an event the subscriber could not have seen is still reported.** The subscription
cannot tell whose event was dropped. The cost is a refetch that turns out to change nothing.

**A loss at the very end of a burst is reported only with the next event.** The buffer drops the
oldest events, so the newest one always arrives, and the skip shows on it.

**Events sent through `ITopicEventSender` directly are not numbered.** They are delivered as
before and neither cause nor hide a skip. Trax's own hooks and the broadcaster handler go
through the publisher.

**The numbers are per node.** Each API node numbers what it publishes to its own subscribers,
which is all a subscription on that node receives.

## Exemplars

- `LifecycleSequenceTests` pins the contract: an unbroken feed counts by one; a loss skips
  exactly one number whatever was lost; a lost event of a hidden train is still reported; a
  filtered event is not a loss; only a jump past the subscription's starting point counts; an
  unnumbered event passes through; the publisher numbers each topic from one and a failed send
  uses no number.
- `SubscriptionStressTests.cs`, in the explicit stress suite that CI does not run, holds it at
  load: under a sustained publish rate every subscriber either receives every event or sees a
  skip in `sequence`. It is evidence, not enforcement; `LifecycleSequenceTests` is the guard.
- [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) is the rule this produces.

Not covered: nothing checks that a client refetches on a skip; that is the client's half.

## Changelog

- **2026-10-02**: The stress suite cited as evidence rather than as a guard, since CI never runs it.
- **2026-09-30**: Recorded.
