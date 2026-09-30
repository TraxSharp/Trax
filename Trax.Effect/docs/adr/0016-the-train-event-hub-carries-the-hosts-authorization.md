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

## Consequences

**`AllowAnonymous()` is allowed, and loud.** A dashboard on a trusted network can still open the
hub; the choice is written at the mapping and logged as a warning at startup. It cannot be combined
with a requirement.

**A host that wants the failure reason in the browser says so.** `WithProjection` on
`UseSignalRHub` produces any payload, including the reason.

**The previous overloads are gone.** A host upgrading from them gets a compile error that points at
the choice it has to make, and the removal is recorded in the package's
`CompatibilitySuppressions.xml`.

## Exemplars

- `SignalRHubAuthorizationTests` pins that mapping without a posture fails at startup, that each
  posture admits and refuses the right clients, that `AllowAnonymous()` logs a warning, and that an
  authorized client receives a failed event without its failure reason.
- [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) is the rule
  this produces.

Not covered: nothing checks that the host's authentication and authorization middleware run before
the hub, and a custom projection can put the failure reason back.

## Changelog

- **2026-09-29**: Recorded.
