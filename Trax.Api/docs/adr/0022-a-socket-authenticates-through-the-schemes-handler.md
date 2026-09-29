---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A socket authenticates through the scheme's handler

A JWT carried in a subscription's `connection_init` payload is authenticated by the scheme's own
`JwtBearerHandler`, run through `IAuthenticationService` against a request built from the upgrade
request with the token as its bearer header. The socket does not repeat the handler's validation
itself. A connection accepted this way is closed with 1008 (policy violation) when its token's
`exp` passes.

The earlier socket path validated the token with a `JsonWebTokenHandler` over a copy of the
scheme's `TokenValidationParameters`. It checked the same signature, issuer, audience and
lifetime, and the docs said the two transports "cannot diverge", but they did: the host's
`JwtBearerEvents` never ran on the socket (an `OnTokenValidated` revocation or tenant check
passed a token HTTP refused), a token signed with a key the cached JWKS did not have was refused
where HTTP refreshes the JWKS and accepts it, and a socket stayed authenticated long after its
token expired.

`JwtSocketAuthentication` holds both halves. The three JWT strategies
(`TraxJwtSchemesSocketInterceptor`, `TraxJwtDispatcherSocketInterceptor`,
`TraxJwtSocketInterceptor`) call it.

## Status

**Accepted.**

## Considered options

**Keep validating on the socket, and add the missing pieces one by one.** Running the events by
hand means building `MessageReceivedContext` and `TokenValidatedContext` and following the
handler's order and result rules, and the JWKS refresh means reproducing its retry. Each is a
copy of the handler that drifts when ASP.NET Core changes it. Running the handler is the only way
the two paths stay the same by construction.

**Authenticate against the upgrade request's own `HttpContext`.** The authentication middleware
already ran on the upgrade, and the handler caches its result per request, so asking again would
return the upgrade's result (usually no token at all), not the payload token's. A request built
for the purpose, in a scope of its own, has no cache to hit.

**Re-validate on every operation instead of closing at `exp`.** An operation on a socket carries
no token, only the connection's principal, so there is nothing to re-validate; and a long-running
subscription would stay open however long it ran. Closing at `exp` bounds the connection by the
credential's own lifetime.

**Close at `exp` plus the clock skew.** HTTP accepts a token for the skew past `exp` because the
issuer's clock and the server's may disagree at the moment of checking. A connection that has
been open for the token's whole lifetime has no such doubt left, so it closes at `exp`.

## Consequences

**What a host set on the bearer options now applies to sockets.** `OnMessageReceived`,
`OnTokenValidated` and `OnAuthenticationFailed` run, and so does claim mapping: a custom
resolver on a socket sees `sub` as `ClaimTypes.NameIdentifier` unless `MapInboundClaims` is off,
as it always did over HTTP.

**A refused connection is told only "Invalid JWT.".** The handler's reason is logged, not sent,
as HTTP answers only 401. A discovery document that could not be fetched is reported the same
way.

**Clients reconnect at expiry.** A client that keeps one socket open for longer than its access
token lives is closed at `exp` and must reconnect with a fresh token. `connection_init` carries
the credential on every reconnect, so a client that already reconnects on close needs nothing
new.

**Revocation mid-connection is still bounded only by `exp`.** Revoking a user or a key does not
close a socket already open; keeping access tokens short-lived is what bounds it.

**The public interceptors' constructors are unchanged**, and the `IOptionsMonitor<JwtBearerOptions>`
they take is no longer read. A host that constructs one directly now needs the scheme registered
as an authentication scheme, since the handler is what runs.

## Exemplars

- `SocketJwtRunsTheSchemeHandlerTests` pins it: a host's `OnTokenValidated` rejection refuses the
  socket, on a single scheme and through the dispatcher; an `OnMessageReceived` failure refuses
  it; a key published after the first connection is found by the JWKS refresh; and a session is
  closed with a policy violation at its token's `exp`, not before.

Not covered: the handler is exercised through the composite interceptor over a fake session,
and the real-socket E2E suites cover acceptance and refusal only; nothing drives a real socket
to its token's expiry.

## Changelog

- **2026-09-27**: Recorded.
