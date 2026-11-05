---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# Subscriptions carry the authorization of the data they stream

The lifecycle subscriptions (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`,
`onTrainCancelled`, `onTrainStateChanged`) and `onDataChanged` stream data that other parts of
the schema already guard, and they are authorized by the same rules, decided per subscriber when
it subscribes.

- **Operations view.** When the operations surface is exposed, a subscriber that satisfies the
  operations gate the host declared (`GateOperations(...)` or
  `GateOperationsToAuthenticatedUsers()`; anyone when the host chose `AllowAnonymousOperations()`)
  receives every train, with the detail `operations.executions` shows, and the data-change
  signals. The endpoint gate is not an operations gate, so with none declared nobody does.
- **Broadcast view.** Anyone else receives only `[TraxBroadcast]` trains whose own posture admits
  them: `[TraxAllowAnonymous]` admits every subscriber, `[TraxAuthorize]` an authenticated one
  meeting its policies and roles. Each event reaches a subscriber only if its train does. A
  failure reason is shown only when the train raised it as a `TrainException`, whose message is
  written for clients; any other is replaced with the message the error filter uses, and host
  detail is withheld. So is a run's `output`, unless the host named the train with
  `AllowOutputForBroadcastSubscribers<TTrain>()`: the posture admits a subscriber to every run,
  not only its own, so an output is withheld until the host says it is meant for all of them.
- A subscriber who could receive nothing is refused when subscribing.
- The decision is made again for the same principal at every connection re-check interval
  ([0033](./0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md)). A
  subscription whose subscriber no longer receives everything it was admitted to ends with
  `TRAX_AUTHORIZATION`, and a check that fails counts as one that does not pass. The socket and
  its other operations stay open. The operator machine feed and a machine's push are asked again
  the same way, by the operations gate and the machine's watch rule.
- `onDataChanged` needs the operations authorization when the surface is exposed, and an
  authenticated caller when it is not.

A `[TraxBroadcast]` train answers the same posture question as a train exposed as a field: on an
open endpoint it must declare `[TraxAuthorize]` or `[TraxAllowAnonymous]`, or the host does not
start.

## Status

**Accepted.**

## Considered options

**`@authorize` on each subscription field.** Refusing at subscribe time fits a field that
streams one kind of data. These fields stream trains with different postures, so no single
directive can say who may receive them, and a directive evaluated per event would send an error
for every event a subscriber may not see instead of leaving it out.

**Refusing every subscriber who fails the operations authorization when operations are
exposed.** Simpler, and it would take the broadcast feed away from every user on a host that also
runs an operations dashboard. Admitting by the operations authorization or the train's posture
serves both.

**Requiring only a posture at startup, without filtering.** It makes the host say whether a
broadcast train is public, but a `[TraxAuthorize(Roles = "Admin")]` train would still reach a
subscriber without the role. Both are needed: the startup rule makes every broadcast train state
a posture, and the filter enforces it per subscriber.

**Filtering in the publishing hooks.** They publish once for every subscriber and do not know who
is listening. The filter has to run on the subscriber's side.

## Consequences

**A `[TraxBroadcast]` train without a posture on an open endpoint now fails at startup.** The
message names the train and the two attributes that answer it.

**Trax's own subscription fields declare how they are authorized.** Each carries
`[AuthorizedPerSubscriber]`, and the type-extension exposure census refuses a field on the
subscription root that declares nothing, Trax's own included. The census runs when a type
extension or `ConfigureSchema` could add a field; Trax's own class is also pinned by a test on
every build.

**An object output is delivered as JSON.** The `output` field returns the JSON value HotChocolate's
`Any` scalar carries, rather than a dictionary it cannot coerce.

## Exemplars

- `SubscriptionAuthorizationTests` pins it: on a host gating operations by role, a Player's and
  an anonymous lifecycle and data-change subscription are refused and an Admin receives every
  train with full detail; a broadcast train gated by role is not delivered to a Player but is to
  an Admin; an anonymous subscriber is refused when only gated broadcast trains exist, and on a
  public one sees events with the failure reason masked unless it came from a `TrainException`
  and without host detail or output; an object output arrives as JSON for a train the host
  opted in, and the operations view receives it without the opt-in; `onDataChanged` without operations
  needs an authenticated subscriber; a broadcast train without a posture fails at startup; every
  field on the subscription root declares its authorization; and the census accepts Trax's own
  fields.
- `SubscriptionRecheckTests` pins the re-check: a lifecycle, step, data-change and operator
  machine subscription ends with `TRAX_AUTHORIZATION` at the re-check after its gate or a
  broadcast train's posture stops admitting it, or after the check throws, and goes on while it
  still passes; over a socket, a subscription that has already received an event ends with a
  `next` frame carrying only the `TRAX_AUTHORIZATION` error, then `complete`, and the socket
  stays open.

Not covered: the principal a subscription re-checks is the one it subscribed with. What changes
between two checks is what the authorization reads (a policy's store, a train's posture); a
change to the principal itself reaches an API-key socket through its key's re-check, and a JWT or
cookie socket at its expiry
([0033](./0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md)). An event
published between a change and the next check is still delivered.

## Changelog

- **2026-10-29**: A subscription's authorization is made again at every connection re-check
  interval, and a subscription that no longer passes ends.
- **2026-10-23**: The broadcast view withholds `output` unless the host opts the train in.
- **2026-10-23**: The operations view follows only a declared operations gate, never the endpoint
  gate (api/0004).
- **2026-10-01**: Not covered now names the bound 0033 puts on a revoked role.
- **2026-09-27**: Recorded.
