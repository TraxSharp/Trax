---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# The endpoint policy applies to every transport

The policy a host sets with `AddTraxGraphQL(g => g.RequireAuthorization(policy))` governs the Trax
schema however an operation reaches it. It is evaluated in HotChocolate's request pipeline, ahead
of the document cache, for every operation: an HTTP request and each query, mutation or
subscription a socket carries pass through the same check, against the principal the transport
established. A principal that is not authenticated never satisfies it. A socket is also checked
once at `connection_init`, so a connection that could run nothing is refused at the handshake.

Every token scheme the host registered authenticates a socket: API keys, the JWT dispatcher, and
every scheme `AddTraxJwtAuth` registered, named or default, when no dispatcher routes between
them.

## Status

**Accepted.**

## Considered options

**One check per transport.** An HTTP request interceptor and a socket session interceptor, each
evaluating the policy. That is what the HTTP path already had, and it is two implementations of
one rule that can drift, with a third needed for the next transport. HotChocolate's request
pipeline is the one place every transport's operations already pass through.

**Checking each socket operation in the session interceptor.** It covers sockets and nothing
else, so the rule would still live in two places.

**Only the handshake check.** A socket's principal is fixed for its lifetime, so the handshake
would decide every operation on it. Kept as an early refusal, but not as the rule: the pipeline
check is what makes the policy a property of the operation rather than of the transport.

**Refusing to start when named JWT schemes have no dispatcher.** It would make every multi-issuer
host add a dispatcher to get sockets working. Trying each registered scheme in turn costs one
signature validation per scheme and runs the host's resolver once, for the scheme that
validated.

## Consequences

**The pipeline check is the only refusal on HTTP.** The HTTP interceptor authenticates the
request with the policy's schemes and refuses nothing. It used to refuse first, by throwing from
`OnCreateAsync`, but HotChocolate then answers without executing, so the request never reached the
diagnostic listeners and the audit trail had no record of a refused request. Refusing inside
execution gives the same response (400, `TRAX_AUTHORIZATION`) and is audited like any other
request.

**A named-scheme socket principal carries its scheme as its authentication type**, the same as
the HTTP path.

## Exemplars

- `EndpointPolicyOnSocketsTests` pins it end to end and at the pipeline: on a host gated by a
  named policy, a key without the policy is refused at `connection_init` and the mutation never
  reaches the scheduler, a key with it runs the mutation; an anonymous `connection_init` on a
  cookie-only gated host is refused; and an operation handed straight to the executor is refused
  for a principal without the policy or an anonymous one, and runs for one with it.
- `AuditRefusedRequestTests` pins that an HTTP request the policy refuses is refused inside
  execution: it reaches the audit sink as an unsuccessful entry.
- `EverySchemeAuthenticatesTheSocketTests` pins named JWT schemes without a dispatcher: a token
  for either scheme is accepted under that scheme, and an empty payload or a token signed with
  the wrong key is rejected.

Not covered: the pipeline check reads the principal from the request's `ClaimsPrincipal` state,
which HotChocolate's own HTTP and socket interceptors set; a custom transport that does not set
it is treated as anonymous and refused.

## Changelog

- **2026-09-30**: The HTTP interceptor no longer refuses; the pipeline check is the only refusal,
  so a refused HTTP request is audited.
- **2026-09-27**: Recorded.
