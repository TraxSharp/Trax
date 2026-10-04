---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A browser socket is accepted only from origins the host serves

A WebSocket upgrade to the Trax GraphQL schema is accepted when its `Origin` header is absent, is
on the endpoint's own host, or is an allowed origin. That holds wherever the schema serves a
socket: at the endpoint `UseTraxGraphQL()` maps, and at one the host maps itself with
`MapGraphQL(path, "trax")`. Anything else gets `403`
before the handshake completes. The allowed origins are the ones passed to
`AllowSocketOrigins(...)` on the Trax GraphQL builder, or, when that is not called, the origins
the host's CORS default policy names or its origin predicate admits. A default policy built with
`AllowAnyOrigin()` names none, so it allows no origin besides the endpoint's own.

## Status

**Accepted.**

## Why this is written down

A browser attaches an `Origin` header to every WebSocket handshake and the server decides what
to do with it. CORS, which a host configures for its HTTP API, does not apply to that
handshake, and neither ASP.NET Core's `UseWebSockets()` without `AllowedOrigins` nor
HotChocolate checks it. Until now the GraphQL endpoint accepted a socket from any origin. The
rule makes the socket follow the same origins the host already serves over HTTP.

## Considered options

**`WebSocketOptions.AllowedOrigins` on the `UseWebSockets()` Trax prepends.** One line, but it
applies to every WebSocket in the application, including a host's own SignalR hubs, and it is
fixed when the pipeline is built, so it cannot fall back to the CORS default policy the host
configures in its own code.

**Checking in the socket interceptor.** The interceptor runs after the handshake has completed.
Refusing there closes a socket that was already accepted; refusing on the upgrade request never
opens one, and answers with a status an operator can see in an access log.

**Checking only in `UseTraxGraphQL()`.** The endpoint convention it adds refuses before
HotChocolate runs, and it stays. On its own it leaves a host that maps the schema itself
unchecked, and HotChocolate's endpoints carry no metadata naming their schema, so no convention
or matcher policy can find the Trax schema's endpoints among a host's others. What is
schema-bound is HotChocolate's server diagnostic event `WebSocketSession`, raised on the Trax
schema's own listeners before the upgrade is accepted. `SocketOriginListener` handles it and,
for an origin that is not allowed, replaces the request's WebSocket feature with one whose accept
answers `403`. That reaches every socket the Trax schema serves and no socket for another schema.
It depends on HotChocolate raising the event before accepting, which the tests below pin.

**Refusing to start when the schema is mapped without `UseTraxGraphQL()`.** Not possible to do
precisely for the same reason, since the schema's endpoints cannot be told apart from a host's
other HotChocolate endpoints, and unnecessary once the listener covers them.

**Requiring an explicit list and refusing to start without one.** Safer on paper, and it would
break every same-origin SPA and every host whose browser clients are already listed in its CORS
policy, which is nearly all of them. The endpoint's own host plus the CORS default covers those
without configuration.

**Treating the CORS default's `AllowAnyOrigin()` as allowing every origin.** That is what it
means over HTTP, where a browser sends no cookies with it: CORS refuses to pair
`AllowAnyOrigin()` with credentials, and ASP.NET Core will not build such a policy. A WebSocket
upgrade has no such switch. The browser attaches the site's cookies to every handshake, so a
socket is always a credentialed request, and it follows what CORS allows for one: an origin the
policy names, or one its predicate admits, and never "any".

**Comparing the scheme for same-origin.** Strict same-origin includes the scheme, but TLS is
commonly terminated in front of the app, so an https page reaches it as http and would be
refused. The host is compared and the scheme is not.

## Consequences

**A host whose browser clients are on another origin, and whose CORS policy is a named one
rather than the default, must call `AllowSocketOrigins(...)`.** Its sockets are refused with
`403` until it does.

**The rule belongs to the Trax schema.** A host's own HotChocolate schema on the same host keeps
its own behaviour.

**A host whose CORS default policy is `AllowAnyOrigin()` and whose browser clients are on
another origin must list them**, with `AllowSocketOrigins(...)` or `WithOrigins(...)`. Until it
does, their sockets get `403`; HTTP is unchanged.

**A request that is not a WebSocket upgrade is untouched.** CORS still governs HTTP.

## Exemplars

- `SocketUpgradeOriginTests` drives the endpoint over a test server: an unlisted origin is
  refused with `403`, the endpoint's own origin (over http and https) and a request with no
  `Origin` are acked, an explicit list and the CORS default policy's origins and origin
  predicate each admit their origins, a default policy allowing any origin admits only the
  endpoint's own (a cookie-authenticated upgrade included), an explicit list replaces the CORS
  default, a
  spelled-out default port matches, an HTTP POST from an unlisted origin is unaffected, and
  `AllowSocketOrigins` rejects anything that is not an origin. The same cases run against a host
  that maps the schema itself, and a second schema on that host is shown to be unaffected.

Not covered: a request whose `Host` the app sees differently from the browser (a proxy that
rewrites it) is compared against what the app sees. A HotChocolate upgrade that stops raising
`WebSocketSession` before accepting would fail the self-mapped tests, not pass silently.

## Changelog

- **2026-10-01**: The CORS default policy's `AllowAnyOrigin()` no longer allows a cross-origin
  socket; only origins the policy names or its predicate admits do.
- **2026-09-27**: Recorded.
