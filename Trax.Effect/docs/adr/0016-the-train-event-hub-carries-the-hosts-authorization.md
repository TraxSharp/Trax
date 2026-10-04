---
authors: [Theauxm]
areas: [auth, platform]
status: accepted
---

# The train-event hub carries the host's authorization

`MapTraxTrainEventHub` maps the SignalR hub through which every connected client receives every
train's lifecycle events. It takes the hub's authorization posture as a required argument
(`RequireAuthorization(...)`, `RequireRoles(...)`, or `AllowAnonymous()`) and the host fails to
start when none is chosen. The sink's default client projection leaves a train's failure reason off
the wire.

## Status

**Accepted.**

## Why this is written down

Because a mapping helper that takes a path and nothing else is the obvious shape, and it is the
shape ASP.NET Core's own `MapHub` has. It leaves the hub's access to whatever the host chains onto
the returned builder, and a host that chains nothing still compiles and runs. Trax's security
posture is that an endpoint handing out data says who may reach it where it is declared, so the
posture is an argument here rather than a convention on the builder. Anyone tempted to restore a
parameterless overload for convenience is undoing this decision.

The failure reason is left out of the default payload for a related reason. It is the text the
failing code put in its exception message, which Trax does not control, and the sink sends each
event to every connection the posture admits, not only to the caller that started the run. A host
that knows its messages are fit for every subscriber can send them with its own projection.

## Considered options

**Default to `RequireAuthorization()`.** Fail-closed, but a host with no authentication configured
then fails at the first connection rather than at startup, and the choice between a policy, roles
and an open hub is still made for the host rather than by it.

**Validate the endpoint's metadata at startup instead of taking the posture as an argument.** Lets
`.RequireAuthorization()` stay on the returned builder, but the check runs only when the endpoints
are first built, which is after startup on a host that never enumerates them. Taking the posture as
an argument fails where the hub is mapped.

**Filter events per connection.** Delivering each client only the runs it may see is the complete
answer, and needs a per-connection notion of ownership the sink does not have yet. The posture and
the masked reason stand on their own and stay useful once filtering exists.

**Refuse a bare `RequireAuthorization()` and require a named policy.** A bare `[Authorize]` on an
endpoint switches the host's fallback policy off and evaluates only its default policy, so a host
whose fallback is stricter than its default would admit more callers to the hub than to an endpoint
with no annotation at all. Refusing the bare call removes that case, but it also removes the plainest
fail-closed posture a host can write ("any signed-in user") and makes every host invent a policy
name for it. Instead, a bare `RequireAuthorization()` applies the default policy **and** the fallback
policy when the host sets one. That is never more open than either of them alone, so the hub is
never more open than an unannotated endpoint, and a host without a fallback sees no change.

## Consequences

**`AllowAnonymous()` is allowed, and loud.** A dashboard on a trusted network can still open the
hub; the choice is written at the mapping and logged as a warning at startup. It cannot be combined
with a requirement.

**A posture is checked where the hub is mapped, not only when a client connects.** Each policy it
names is resolved through the host's `IAuthorizationPolicyProvider` at `MapTraxTrainEventHub`, and an
unknown one stops the host there. A role name containing a comma is refused, because
`AuthorizeAttribute.Roles` splits on commas and would read it as several roles.

**Admission does not outlive the credential.** Authorization runs when a connection opens, so the hub
sets `CloseOnAuthenticationExpiration`: a connection is closed once the authentication it was
admitted on expires. It is set after the host's `ConfigureConnection`, which cannot turn it off. A
user revoked before their credential expires is still connected until it does; that bound is the
credential's lifetime, which the host controls.

**A browser reaches the hub only from an origin the host serves or allows.** A request to the hub
(negotiate, a WebSocket upgrade, Server-Sent Events, long polling) that carries an `Origin` header is
refused with `403`, whatever its credentials, unless the origin is on the hub's own host or is
allowed: by `AllowOrigins(...)` when the host calls it, otherwise by the host's CORS default policy.
With neither, only the hub's own origin is. A request with no `Origin`, which is what a non-browser
client sends, is left to the posture. This is the rule Trax.Api applies to its GraphQL sockets
(`AllowSocketOrigins`), for the same reason: a browser sends a site's cookies with a request another
site makes, and a WebSocket is not covered by CORS.

**Access is still all-or-nothing per connection.** A connection the posture admits receives every
train's events. Per-connection filtering is the open alternative above.

**A host that wants the failure reason in the browser says so.** `WithProjection` on
`UseSignalRHub` produces any payload, including the reason.

**The previous overloads are gone.** A host upgrading from them gets a compile error that points at
the choice it has to make, and the removal is recorded in the package's
`CompatibilitySuppressions.xml`.

## Exemplars

- `SignalRHubAuthorizationTests` pins that mapping without a posture fails at startup, that each
  posture admits and refuses the right clients, that `AllowAnonymous()` logs a warning, and that an
  authorized client receives a failed event without its failure reason. It also pins that a bare
  `RequireAuthorization()` keeps the host's fallback policy (over long polling, and over WebSockets
  with negotiation skipped), that an unknown policy or a role name with a comma fails at mapping, and
  that a connection whose authentication expires is closed even when the host tries to turn that off.
  It pins that a client from a foreign origin is refused with `403` even with valid credentials, on
  negotiate and on a WebSocket without negotiation, and that the hub's own origin, an origin given to
  `AllowOrigins` and one the CORS default policy allows are admitted.
- [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) is the rule
  this produces.

Not covered: nothing checks that the host's authentication and authorization middleware run before
the hub, and a custom projection can put the failure reason back.

## Changelog

- **2026-10-03**: A browser reaches the hub only from its own origin or one the host allows.
- **2026-09-30**: A bare `RequireAuthorization()` applies the fallback policy with the default one,
  named policies are resolved at mapping, a role name with a comma is refused, and a connection is
  closed when its authentication expires.
- **2026-09-29**: Recorded.
