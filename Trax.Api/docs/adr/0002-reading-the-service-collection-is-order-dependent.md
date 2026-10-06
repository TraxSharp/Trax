---
authors: [Theauxm]
areas: [graphql, platform]
status: accepted
---

# Reading the service collection during registration is order-dependent

Inspecting the `IServiceCollection` inside a registration extension answers "is this
registered **yet**", not "will this be registered". Every existing site that does it is
enumerated with a count and a reason. Three kinds are safe: an idempotency guard, a
precondition that throws, and a decision paired with a startup validator
([0001](./0001-a-misconfigured-host-fails-at-startup.md)).

Inspecting the collection is not banned. Being **silently** different because of order is.

Two sites used to be none of the three. Both have since been made safe, and no site in
the census is outside the three kinds today.

The broadcaster branch in `AddTraxGraphQL()` used to wire the GraphQL train-event handlers
only when an `ITrainEventReceiver` was already registered, so a receiver registered
afterwards left them unwired with no error. The handlers are now registered unconditionally.
Only `TrainEventReceiverService` resolves them, and it exists only when a receiver does, so
the question disappeared rather than being detected.

Train discovery is a decision paired with a startup validator. `AddTraxGraphQL()` snapshots
train discovery to check each exposed train's posture and name and to decide whether
`RootQuery` and `RootMutation` get any fields. A `[TraxQuery]`, `[TraxMutation]` or
`[TraxBroadcast]` train registered afterwards skips those checks, so
`TrainRegistrationOrderValidator` compares the trains the finished container exposes with the
snapshot and refuses the host at startup, naming each train it missed. The established shape
is still `AddTrax(...)` then `AddTraxGraphQL(...)`, with train registration finished inside or
before `AddTrax`; the validator makes any other order fail loudly.

## Status

**Accepted.**

## Why this is written down

Because it has shipped as a bug twice, from the same line of reasoning both times.

`AddTraxGraphQL()` picked the application services to bridge into HotChocolate's schema
container by reading the collection. A host calling `AddAuthentication()` afterwards got no
bridge, and the authentication interceptor then failed to activate on every request.

Worse, the same pattern chose the subscription interceptor. A scheme registered afterwards
was invisible, so none was wired, and HotChocolate's default accepted every
`connection_init`. **Subscriptions stopped authenticating while HTTP kept working**, because
`@authorize` lives on the schema and does not depend on registration order. Nothing warned.

## Consequences

**Prefer restructuring so the question disappears.** `ApplicationServiceBridge` registers a
forwarding factory that resolves on first use instead of asking whether a service exists
yet, which makes the ordering question moot rather than merely detected. That is better than
a validator, because there is nothing left to get wrong.

**Where order must matter, it fails at startup and the message names the call to
move.** The subscription interceptor was the example here until
[0006](./0006-one-socket-interceptor-composes-every-token-scheme.md) restructured it: one
composite is registered for every host and reads the schemes from the finished container, so
the token-based schemes may be registered on either side of `AddTraxGraphQL()`.

**The census ratchets one way.** Adding a site means making it safe first and recording why.
Raising a count to silence the guard is itself the violation, and the guard says so.

## Exemplars

- `NoSilentRegistrationOrderDependenceTests` enumerates every introspection site in `src/`
  with the count it reads at, fails on a new file or a raised count, and separately fails
  when a reviewed entry names a file that no longer exists.
- [Registration Order](/docs/reference/registration-order) carries the same rule for users.

Not covered:

- **The verb list is closed.** It matches `Any`, `All`, `Count`, `Where`, `Select`, and
  `First`, `Last` and `Single` each with and without their `OrDefault` form. A `foreach` over
  the collection, `Contains`, `IndexOf`, `OfType<T>()` or an indexer is invisible, and
  `TrainDiscoveryService` uses exactly the `foreach` form.
- **The receiver must be named `services` or `Services`.** A field-backed `_services` or any
  other name is not seen. A helper taking a parameter named `services` *is* counted, which is
  how the train-discovery resolver got into the list.
- **It scans this repo's `src/` only.** The largest collection read that `AddTraxGraphQL()`
  triggers executes in `Trax.Mediator`, where no census exists.
- **`Trax.Dashboard` ships `AddTraxDashboard()` and reads the collection too**, and has no
  census at all. The policy is stated workspace-wide; the guard is not.

## Changelog

- **2026-10-05**: Corrected the two accepted exceptions, both stale. The broadcaster branch
  was removed on 2026-09-24 (the handlers are registered unconditionally), and a train
  registered after `AddTraxGraphQL()` is refused at startup by
  `TrainRegistrationOrderValidator` rather than silently absent. No site is outside the three
  safe kinds.
- **2026-09-27**: The subscription interceptor branches are gone, restructured away by
  [0006](./0006-one-socket-interceptor-composes-every-token-scheme.md), so the token-based
  schemes no longer have to precede `AddTraxGraphQL()`. The census count for
  `GraphQLServiceExtensions.cs` drops from five to two.
- **2026-09-12**: Withdrew the "fourth safe kind". `ITrainEventReceiver` is public and on
  the API baseline, so a host can register one after `AddTraxGraphQL()` and leave the
  handlers unwired; the broadcaster branch is a second accepted exception, not a safe kind.
- **2026-09-11**: Counted the broadcaster branch. Two sites are outside the three safe
  kinds, not one.
- **2026-09-11**: Recorded.
