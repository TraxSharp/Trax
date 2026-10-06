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

**A refused socket and a failed event are audited too.** A socket refused at `connection_init`
never reaches the execution pipeline, so the composite socket interceptor reports it to the audit
package, which records one failed entry with `TRAX_SOCKET_REFUSED`, the scheme that judged it and
the reason, never the credential. An event of an accepted subscription that fails, in a resolver
or in its source, is recorded as its own failed entry marked `subscriptionEvent=error`: one entry
per failing event, however many of its fields failed, with the errors joined into `ErrorText` up to
`MaxErrorTextLength` and their number in `subscriptionEventErrors`. A successful event is still not
recorded, for the reason above.

**The introspection skip follows the same rule.** An introspection query that raised an error is
audited; one that succeeded is skipped while `SkipIntrospection` is on.

## Exemplars

- `AuditRefusedRequestTests` pins it end to end: a subscription the operations gate refuses over
  HTTP is audited under the default options, beside a request the endpoint gate refuses; a socket
  refused at `connection_init` with an unknown key or none is audited without the key; and a
  subscription whose second event throws leaves exactly one entry, and one whose two events each
  fail in 200 aliased fields leaves exactly two.
- `TraxGraphQLAuditListenerTests` pins it in the listener: a subscription refused when subscribing,
  one whose event source throws and one that fails validation are each audited with
  `SkipSubscriptions` on; an accepted one is not.

Not covered: a host that replaces Trax's socket interceptor through `ConfigureSchema` gets no
entry for the handshakes its own interceptor refuses.

## Changelog

- **2026-10-05**: A failing event is one entry, its errors joined, rather than one entry per error.
- **2026-10-05**: A socket refused at `connection_init` and an error in a later event of an
  accepted subscription are audited; both were listed as not covered.
- **2026-10-01**: Recorded.
