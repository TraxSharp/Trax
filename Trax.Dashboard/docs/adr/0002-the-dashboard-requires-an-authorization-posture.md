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
thing that can be missing. Authentication is still the host's: with no scheme that can
challenge, a gated request fails rather than being served.

**A circuit is authorized once.** The posture is checked on the page request and on the hub's
negotiate and connect. Navigation inside an established circuit does not go back through the
endpoint, which is how every Blazor Server app behaves; all dashboard pages share the one
posture, so there is nothing a page further in could need that the circuit did not.

## Exemplars

- `DashboardAuthorizationTests` pins the refusal without a posture, the posture on every page
  and hub endpoint, the startup failure for an unregistered policy, the warning for
  `AllowAnonymousDashboard()`, the contradiction between the two, and that the returned builder
  composes.
- [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) is the rule this
  produces.

Not covered: nothing asserts what a request actually receives (a challenge, a 403); the tests
read endpoint metadata, and the authorization middleware that acts on it is ASP.NET's.

## Changelog

- **2026-09-27**: Recorded.
