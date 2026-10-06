---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# The dashboard refuses to start without an authorization posture

`UseTraxDashboard()` maps nothing until the host has chosen who may use the dashboard, in
`AddTraxDashboard(o => ...)`: `RequirePolicy(name)`, `RequireRoles(...)`, or
`AllowAnonymousDashboard()`. The choice is applied to every endpoint `MapRazorComponents<App>()`
contributes, which is the pages and the Blazor circuit hub (`/_blazor`), and
`UseTraxDashboard()` returns that endpoint convention builder so the host can add conventions
of its own. Until this, the dashboard applied no authorization and said nothing, which
[0001](./0001-the-dashboard-mounts-into-the-host-app.md) recorded as the consumer's job.

## Status

**Accepted.** Narrows [0001](./0001-the-dashboard-mounts-into-the-host-app.md): the dashboard
still mounts into the host and still owns no identity, but it no longer maps with the gate open
by default.

## Considered options

**Keep it the host's job, and document it.** That is what 0001 recorded. Under the ordinary
host shape (`[Authorize]` on its own controllers, no fallback policy) the dashboard, which
queues, runs and cancels trains and edits scheduler settings, then mapped with no
authorization at all. The GraphQL operations namespace in Trax.Api already refuses to start
without a posture; the dashboard now does the same for the same class of surface.

**Return the convention builder and let the host call `RequireAuthorization()` on it.** It is
the idiomatic ASP.NET shape, and it is kept, but on its own it cannot fail closed: the host
adds the convention after `UseTraxDashboard()` returns, so nothing at startup can tell a host
that forgot from one that has not got there yet. The posture is therefore declared in the
options, where `UseTraxDashboard()` can see it, and the returned builder is for anything
further.

**A parameterless "any authenticated user" posture.** Not offered. On a host with public sign
up it is the same as anonymous, and it is the default a host reaches for without thinking.
A host that means it registers a policy that requires an authenticated user and names it.

**`AllowAnonymousDashboard()` adds `[AllowAnonymous]`.** Rejected: that would override a host's
fallback policy, which is one of the gates 0001 names. The opt-in instead means "the dashboard
adds no authorization of its own", so a fallback policy or an ingress rule still applies, and
`UseTraxDashboard()` logs a warning on every start while it is set.

## Consequences

**Static assets stay outside the posture.** `MapStaticAssets()` maps the host's whole static
asset manifest, not only the dashboard's CSS and JavaScript, and gating it would gate the
host's own login page. The dashboard's assets are the same files the NuGet package ships.

**A named policy that is not registered fails at startup**, not on the first request.
`AddRazorComponents()` registers the authorization services, so the policy name is the only
thing that can be missing. Authentication is still the host's, but a posture that could only
answer a refusal with a 500 fails at startup too: a policy naming an authentication scheme that
is not registered; a policy without schemes, or roles, on a host with no default challenge
scheme; and a default forbid scheme the host named that is not registered, through which a
signed-in caller the posture refuses would be forbidden. A roles-only posture is not combined
with the host's default authorization policy, so that policy's schemes play no part.

**A circuit stays authorized only while its user satisfies the posture.** The endpoint checks
the posture on the page request and on the hub's negotiate and connect, and navigation inside
an established circuit does not go back through it. So the dashboard re-checks the same policy
and roles inside the circuit, against the host's `AuthenticationStateProvider`: when the
dashboard's root component attaches the circuit, whenever the provider reports a change, every
minute, and before each persisted-operation write. Before every inbound circuit message (a
click, a change, an interop call) a `CircuitHandler` reads the latest verdict, and once the
user is refused it throws, which makes the framework close the circuit, and the page reloads
through the endpoint. Refusing at the message rather than relying on the redirect means a
client that ignores the redirect gets nothing more done. A failure to evaluate (no provider, a
policy handler that throws) is a refusal.

Three limits come with it. **The dashboard registers no `AuthenticationStateProvider`** and
replaces none: the provider is shared by every circuit on the host, and a library that swapped
it would silently change the host's own components. It reads whatever the host registered, so
it sees a revoked role or a signed-out user only when that provider does. ASP.NET Core's
default, `ServerAuthenticationStateProvider`, holds the principal the connection arrived with
and never refreshes it; a host that wants revocation within a circuit registers a revalidating
provider (ASP.NET Identity's template does) or names a policy whose handlers read live state,
which the interval re-runs. **The circuit check has no `HttpContext`**: a policy handler that
reads it as the resource gets `null`, and a policy's authentication schemes are not
re-authenticated. **Only the posture is re-checked**: conventions a host adds on the builder
`UseTraxDashboard()` returns are checked at the endpoint only.

## Exemplars

- `DashboardAuthorizationTests` pins the refusal without a posture, the posture on every page
  and hub endpoint, the startup failure for an unregistered policy, the warning for
  `AllowAnonymousDashboard()`, the contradiction between the two, the startup failures for a
  posture with no scheme to challenge or forbid with, and that the returned builder composes.
- `CircuitAuthorizationTests` pins the in-circuit half: a user whose role is revoked mid-circuit
  has the next inbound message refused, an authentication change is acted on without waiting
  for the interval, a live policy is re-run, a write checks first, a refused circuit reloads, a
  circuit the dashboard never attached is left alone, a missing provider fails closed, and the
  dashboard registers no `AuthenticationStateProvider`.
- [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) is the rule this
  produces.

Not covered: nothing asserts what a request actually receives (a challenge, a 403); the tests
read endpoint metadata, and the authorization middleware that acts on it is ASP.NET's. Nor does
anything run a real circuit: the tests drive the circuit handler's pipeline directly, so that
the framework closes a circuit whose inbound activity throws is ASP.NET's behaviour, not pinned
here. The write-time check is wired only into the persisted-operation pages; the other writes
rely on the per-message check.

## Changelog

- **2026-10-05**: A posture with no authentication scheme to challenge with, a policy naming an
  unregistered scheme, or an unregistered default forbid scheme fails at startup.
- **2026-10-01**: Replaced "a circuit is authorized once". The posture is now re-checked inside
  the circuit, on authentication changes, on an interval and before each inbound message, and
  a refused circuit is closed. Recorded why the dashboard reads the host's
  `AuthenticationStateProvider` rather than registering one.
- **2026-09-27**: Recorded.
