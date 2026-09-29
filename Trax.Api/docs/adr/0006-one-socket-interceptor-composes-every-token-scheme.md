---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# One socket interceptor composes every token scheme

HotChocolate runs one `ISocketSessionInterceptor` per schema. `AddTraxGraphQL()` used to pick
one of three from what the `IServiceCollection` held when it ran, so a host with both API-key
and JWT auth could authenticate subscriptions with only one of them, and a scheme registered
after `AddTraxGraphQL()` got no interceptor at all. It now registers
`TraxCompositeSocketInterceptor` for every host. The composite reads the registered schemes
from the finished container on the first connection and hands each `connection_init` to the
API-key, JWT or JWT-dispatcher strategy.

## Status

**Accepted.**

## The rules it follows

**Routing.** With one scheme, every connection goes to it, so a single-scheme host sees no
change. With both, an `authToken` whose header parses as a JWT goes to JWT validation and any
other `authToken` to the API-key resolver; without an `authToken`, `apiKey` means API key and
`bearer` means JWT. `authToken` wins because it is what each strategy reads first, so the
credential routed on is the credential validated.

**One strategy per credential.** A JWT that fails validation is rejected. It is not retried as
an API key, and an unknown API key is not retried as a JWT.

**No token scheme registered: accept.** That is what HotChocolate's default does, and what a
host with public subscriptions or cookie authentication (which authenticates the upgrade
request itself) relies on.

## Considered options

**Try every strategy until one accepts.** No routing rule to explain, but every failed
connection runs every resolver, and the host's resolvers typically hit a database. It also
makes the rejection message belong to whichever strategy ran last.

**Route on the three-segment shape alone.** An opaque API key containing two dots would go to
JWT validation and be rejected. Parsing the header costs one base64 decode and keeps those keys
working.

**Keep the startup refusal for late registration.** It closed the fail-open, but left the
capability gap and kept an ordering rule hosts had to learn. Deciding from the finished
container makes the ordering question disappear, which is what
[0002](./0002-reading-the-service-collection-is-order-dependent.md) prefers to a validator.

## Consequences

**`TraxSubscriptionAuthWiringValidator` stays, as a backstop.** It no longer checks ordering. It
refuses to start a host where a token scheme is registered and HotChocolate's default
interceptor is the active one, which only a regression in Trax or a host deliberately
registering the default can produce.

**A host-supplied interceptor still replaces the composite.** `ConfigureSchema` runs after
Trax's wiring, so the host's registration is the last one and wins, as before.

**The existing three interceptor types stay public** and keep working when a host registers
one directly. The composite constructs them per scheme rather than duplicating their
validation.

## Exemplars

- `TraxCompositeSocketInterceptorTests` drives every routing branch against real
  registrations: both credentials in each field, neither, an unknown key, a JWT with the wrong
  key, a dotted API key, the dispatcher alongside an API key, each scheme alone, and no scheme.
- `SubscriptionE2ETests` does the same over a real WebSocket for a host with both schemes, with
  a no-scheme host as the negative control.
- `RegistrationOrderTests` pins that a scheme registered after `AddTraxGraphQL()` is enforced,
  and that the default interceptor with a scheme registered refuses to start.

Not covered: the rule that a host-supplied interceptor replaces the composite depends on
HotChocolate resolving the last registration. `CustomSocketInterceptorE2ETests.cs` pins that for
the current version, and nothing else would notice a change in it.

## Changelog

- **2026-09-27**: Recorded.
