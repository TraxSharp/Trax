---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A socket connection has a maximum lifetime, and an API-key socket re-checks its key

Every WebSocket connection to the Trax schema is closed at its maximum lifetime, one hour unless
the GraphQL builder sets another with `MaxConnectionLifetime(...)` (at most one day), however it
authenticated. A JWT connection still closes earlier at its token's `exp`
([0022](./0022-a-socket-authenticates-through-the-schemes-handler.md)), and a connection the
upgrade request authenticated (a cookie) closes when that sign-in's `ExpiresUtc` passes. An
API-key connection's key is resolved again every five minutes, or the interval set with
`RecheckConnectionCredentialsEvery(...)` (at most one day), and the connection is closed as soon
as the key no longer resolves to the principal it connected as: the same id, type, roles and
claims. A resolver that fails during a re-check closes it too.

The lifetime close is code 1001 (Going Away); a credential that expired or no longer holds closes
with 1008 (policy violation). `SocketConnectionLifetime` keeps one timer per connection for all
of them, and the earliest deadline wins.

## Status

**Accepted.**

## Why this is written down

A connection is authenticated once, at `connection_init`, and an operation on it carries no
credential. Before this, only a JWT socket had an end: an API-key or cookie socket streamed for as
long as the client kept it open, whatever happened to the key or the sign-in.

## Considered options

**Document it and leave revocation to the host.** The host has no hook to find the sockets a key
opened, so documenting it would only describe a gap the host cannot close.

**Re-check on every operation.** An operation carries nothing to re-check, and a subscription
that runs for hours is one operation, so it would never be checked again. A timer is what bounds
a subscription already running.

**Re-check every credential, not only API keys.** A JWT has an `exp`, and re-running its handler
mid-connection would need the token kept in memory for the connection's life. A cookie is
re-validated by the cookie handler only when a request carries it, which a socket never does
again; its expiry is the bound, as SignalR's `CloseOnAuthenticationExpiration` reads it. An API
key has neither, so it is the one that is re-resolved, which is what ASP.NET Core Identity's
`SecurityStampValidator` does for a cookie on an interval (30 minutes by default; five here,
because a key is usually a machine credential and a lookup is cheap).

**Close on any change to the resolved principal.** The display name is not compared: it grants
nothing, and closing a socket because a label changed would only cost the client a reconnect.
Roles and claims are compared, because a socket keeps the principal it connected as, and a role
taken from a key should not stay with its open socket.

**Keep the socket open when the resolver fails.** A failed re-check cannot tell a revoked key
from a store that is down. Trax fails closed: the connection closes, and the client's reconnect
is refused or accepted on the resolver's next answer.

**No limit by default, or an unlimited setting.** Fail-closed means every connection has an end.
API Gateway caps a WebSocket at two hours for the same reason. The one-day ceiling keeps "a very
large value" from becoming the unlimited setting by another name.

**Going Away for the lifetime, policy violation for a credential.** graphql-ws clients retry
after 1001 and treat 1008 as fatal. A connection that reached its lifetime has nothing wrong with
its credential and should reconnect on its own; one whose key was revoked, or whose token or
sign-in expired, needs the application to obtain a new credential first.

## Consequences

**A client that keeps one socket open for more than an hour is closed and must reconnect.** A
graphql-ws client with retries on does that by itself; its subscriptions restart, and an event
published in the gap is not replayed.

**Each open API-key connection costs one resolver call per re-check interval.** A host with many
long-lived API-key sockets and an expensive resolver can lengthen the interval, up to a day.

**A host that replaces the socket interceptor through `ConfigureSchema` loses the lifetime and
the re-check** with the rest of the composite, as
[0006](./0006-one-socket-interceptor-composes-every-token-scheme.md) describes for
authentication. `TraxApiKeySocketInterceptor` registered on its own still re-checks its key at the
default interval.

## Exemplars

- `SocketConnectionLifetimeTests` pins it: an API-key socket closes within one re-check interval
  of its key being revoked, of its roles changing, or of its resolver failing, and stays open on a
  display-name change; a socket whose key keeps resolving, one with no token scheme, and a cookie
  socket whose sign-in outlives the lifetime all close with Going Away at the lifetime; a cookie
  socket closes with policy violation at its sign-in's expiry; a refused connection is never
  closed later; the API-key interceptor on its own re-checks; the builder defaults are one hour
  and five minutes and nonsensical values are refused; a JWT socket whose token outlives the
  lifetime closes at the lifetime; and a real socket is closed with 1001 at a one-second lifetime.
- `SocketJwtRunsTheSchemeHandlerTests.cs` still pins the close at `exp`, now through the same
  timer.

Not covered: the tests drive the timer with a manual clock over a fake connection, except the
one real-socket case. Nothing checks that a client reconnects after 1001; that is the client's
behaviour.

## Changelog

- **2026-10-01**: Recorded.
