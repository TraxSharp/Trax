---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A refused request is always audited

The audit pipeline can skip two kinds of request, introspection and subscriptions, and both skips
are on by default. They apply only to a request that succeeded. A request that was refused or
failed is audited whatever the options say: a subscription that authorization refused when it
subscribed, one whose event source threw, an introspection query that raised an error. For a
subscription, succeeded means it returned a stream of events.

## Status

**Accepted.**

## Considered options

**Skip every subscription (what shipped first).** Subscriptions do not fit a request and response
record: one subscription can stream for hours. But the skip was decided from the operation type
alone, so a socket that subscribed again and again without the right credentials left no record of
any attempt, and a refusal is the event an audit trail most needs. OWASP's logging guidance lists
authorization failures first among the events to record.

**Keep the skip and document it.** A host would have to turn the skip off to see refusals, and
then pay for an entry per accepted subscription it never wanted.

**Record every subscription event.** The listener sees each event through HotChocolate's
`OnSubscriptionEvent`, but an entry per event floods the sink with data the host already streams,
and says nothing more about who asked for what than the entry for the subscribe step does.

## Consequences

**Refusals are audited the same way on every transport.** An HTTP request the endpoint gate refuses
and a subscription a lifecycle feed refuses each leave one entry with `Success = false` and the
error code in `ErrorText`.

**The introspection skip follows the same rule.** An introspection query that raised an error is
audited; one that succeeded is skipped while `SkipIntrospection` is on.

## Exemplars

- `AuditRefusedRequestTests` pins it end to end: a subscription the operations gate refuses over
  HTTP is audited under the default options, beside a request the endpoint gate refuses.
- `TraxGraphQLAuditListenerTests` pins it in the listener: a subscription refused when subscribing,
  one whose event source throws and one that fails validation are each audited with
  `SkipSubscriptions` on; an accepted one is not.

Not covered: an error in a later event of an accepted subscription is not audited, because the
entry for that subscription was decided when it subscribed. A socket that is refused at
`connection_init` never reaches the execution pipeline, so it leaves no audit entry.

## Changelog

- **2026-10-01**: Recorded.
