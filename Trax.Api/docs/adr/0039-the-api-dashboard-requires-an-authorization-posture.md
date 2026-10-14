---
authors: [Theauxm]
areas: [auth, platform]
status: accepted
---

# The API dashboard requires an authorization posture

`UseTraxApiDashboard()` serves the React operations dashboard that Trax.Api.Dashboard carries, and
it maps nothing until the host has said who may load it: `RequirePolicy(name)`,
`RequireRoles(...)`, or `AllowAnonymousShell()`. The posture is declared in the call's options and
applied to the dashboard's one endpoint, so its page, every route of the app and every file it
loads are gated alike; the returned convention builder takes anything further. This is the same
rule the Blazor dashboard follows (`Trax.Dashboard/docs/adr/0002`), for the same class of surface.

## Status

**Accepted.**

## Considered options

**Serve it open, because the shell holds no data.** The app is static HTML and JavaScript; every
read and write it makes goes to the GraphQL operations surface, which is gated on its own
([0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md)). True, and it is
why the anonymous opt-in exists at all. Rejected as the default: an admin UI that anyone can load
advertises the admin surface, puts the operator's credential prompt on a page the host never
chose to expose, and is the default a host would ship without thinking. Trax's defaults fail
closed.

**Let the host chain `RequireAuthorization()` on the returned builder, and check for it.** The
idiomatic ASP.NET shape. It cannot fail at startup: the convention is added after the call
returns, so the call cannot tell a host that forgot from one that has not got there yet, and a
check at the first request is a check after the host is serving. The posture therefore goes in
the options, where the call sees it; the builder is still returned for further conventions.

**`AllowAnonymousShell()` adds `[AllowAnonymous]`.** Rejected, as for the Blazor dashboard: it
would override a host's fallback policy. The opt-in means "the dashboard adds no authorization of
its own", so a fallback policy or an ingress rule still applies, and a warning is logged on every
start while it is set. It is named for what it opens, the shell, because the data behind it stays
gated.

## Consequences

**A posture that cannot answer a refusal fails at startup**, as the Blazor dashboard's does: a
policy that is not registered, a policy naming a scheme that is not registered, no scheme to
challenge with, or a default forbid scheme that is not registered.

**A gated file is never kept by a shared cache.** The page is `private, no-cache`; content-hashed
files are `private` unless the shell is anonymous and nobody signed in.

**The posture needs a scheme a browser carries.** Loading a page sends cookies, never an
`X-Api-Key` or `Authorization` header, so a host whose only scheme is an API key or a bearer token
cannot gate the shell with it: such a host uses a cookie or OpenID Connect sign-in for the shell,
or `AllowAnonymousShell()` with something in front of it. The app itself still authenticates to
GraphQL with the key or token the operator enters, as it always has.

## Exemplars

- `ApiDashboardHostTests` pins the refusal without a posture, the contradiction between the
  opt-in and a policy, the startup failures for an unregistered policy and for a posture with no
  scheme to challenge with, the posture on the page, a deep link and an asset (401, 403, 200), the
  warning and the fallback policy staying in force under `AllowAnonymousShell()`, that the
  returned builder adds to the posture, and that gated files are cached `private`.
- [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) is the rule this
  produces.

Not covered: nothing re-checks the posture after the page has loaded. The app is static once
loaded, and everything it does afterwards is a GraphQL request, authorized there.

## Changelog

- **2026-10-13**: Recorded.
- **2026-10-13**: Gated files are cached `private`.
