---
title: UseTraxApiDashboard
description: "Reference for UseTraxApiDashboard, which serves the React operations dashboard from a Trax.Api host behind a required authorization posture."
type: reference
area: dashboard
package: Trax.Api.Dashboard
namespace:
  - Trax.Api.Dashboard.Extensions
  - Trax.Api.Dashboard.Configuration
parent: API Dashboard
grand_parent: SDK Reference
nav_order: 1
source:
  - Trax.Api/src/Trax.Api.Dashboard/Extensions/ApiDashboardEndpointExtensions.cs
  - Trax.Api/src/Trax.Api.Dashboard/Configuration/TraxApiDashboardOptions.cs
---

# UseTraxApiDashboard

Serves the Trax operations dashboard, the single-page app `Trax.Api.Dashboard` carries, at a
path of the host (`/trax/dashboard` by default). It is an extension method on
`IEndpointRouteBuilder`, in `ApiDashboardEndpointExtensions`, so it is called on the built
`WebApplication`, or on a route group.

```csharp compile
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;

builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
builder.Services.AddAuthorization(o =>
    o.AddPolicy("TraxAdmin", p => p.RequireRole("Admin")));
// A scheme the browser carries on a page load, such as a sign-in cookie, gates the app.

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.RequirePolicy("TraxAdmin"));
```

## Signature

```csharp fragment
public static IEndpointConventionBuilder UseTraxApiDashboard(
    this IEndpointRouteBuilder endpoints,
    Action<TraxApiDashboardOptions> configure,
    string path = "/trax/dashboard"
)
```

| Parameter | Description |
|-----------|-------------|
| `configure` | Chooses the authorization posture, and optionally the GraphQL path. Required. |
| `path` | Where the app is served. Leading and trailing slashes are normalized; `/` and a path with route parameters are refused, because everything under the path belongs to the app. |

It returns the endpoint's convention builder, so the host can add conventions of its own
(`RequireHost`, rate limiting, a further `RequireAuthorization`). They add to the posture; they
do not replace it.

## It refuses to start without a posture

The dashboard is an admin UI for queueing, running and cancelling trains, so the host has to say
who may load it, in `configure`:

| `TraxApiDashboardOptions` | Effect |
|---------------------------|--------|
| `RequirePolicy("<name>")` | Every request for the app (its page, any route of it, every file it loads) requires the named policy. The policy must be registered with `AddAuthorization`, or the call throws at startup. |
| `RequireRoles("<role>", ...)` | Every request for the app requires one of the roles. Combined with `RequirePolicy`, both apply. |
| `AllowAnonymousShell()` | The app adds no authorization of its own, and a warning is logged on every start. A fallback policy or an ingress rule in front of it still applies. |

With none of them, `UseTraxApiDashboard` throws `InvalidOperationException` naming the three, and
`AllowAnonymousShell()` together with either of the others throws as a contradiction. A policy or
roles posture also fails at startup when the host has no authentication scheme to challenge a
caller with, when the policy names a scheme that is not registered, or when the default forbid
scheme is not registered, because every refusal would otherwise be a 500. This is the same rule
the Blazor dashboard follows ([UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)),
recorded in `Trax.Api/docs/adr/0039`.

**The posture gates the app, not the data.** The app holds no data. Everything it shows or changes
is a request to the Trax GraphQL endpoint, authorized there by the endpoint's own policy and the
`operations` gate, whatever the posture here. That is why `AllowAnonymousShell()` exists: it opens
the page and its scripts, nothing more.

**Use a scheme the browser carries.** Loading a page sends cookies, never an `X-Api-Key` or
`Authorization` header, so a policy whose only scheme is an API key or a bearer token refuses
every page load. Gate the app with a cookie or OpenID Connect sign-in, for example
`RequirePolicy(OidcDefaults.PolicyName)` after
[AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth), or, when the host
authenticates only by key or token, use `AllowAnonymousShell()` with the host's network or
ingress as the gate.

## How the app authenticates

Once loaded, the app asks the operator for an API key or a bearer token, keeps it in the
browser's `localStorage` for the host's origin, and sends it on every GraphQL request
(`X-Api-Key`, or `Authorization: Bearer`) and in the subscription socket's `connection_init`
payload (`apiKey` or `authToken`), which the GraphQL endpoint's socket interceptor reads. The
endpoint is on the same origin as the app, so CORS is not involved, and a socket from the host's
own origin passes the GraphQL endpoint's origin check. Anything else served from the same origin
can read that `localStorage`, so serve the dashboard from a host that does not serve untrusted
pages.

## What it serves

| Request | Answer |
|---------|--------|
| `{path}` | A redirect to `{path}/`, keeping the query string. |
| `{path}/` and any route of the app, such as `{path}/executions/42` or `{path}/trains/Trax.Samples.Orders.IShipOrderTrain` | The app's page, with `Cache-Control: no-cache` and an ETag. |
| `{path}/assets/<name>-<hash>.js` and the app's other files | The file, with its content type. Files under `assets/` are named by their content and sent with `Cache-Control: public, max-age=31536000, immutable`; the others are revalidated. |
| Any other path under `{path}/assets/`, or one whose last segment ends in a lower-case file extension (`.js`, `.png`, `.json`) | `404`. A missing script is never answered with the page. The dots in a .NET name, such as a train's, are not an extension. |

`GET` and `HEAD` are served, and a matching `If-None-Match` is answered with `304`.

The page is written for the path it is served at: a `<base href>` at the app's base path, which
the app's relative asset URLs resolve against, and the runtime config it reads at startup, both
inserted first in its `<head>`. So one build works under any path, a route group, or a
`UsePathBase` prefix.

## GraphQL path

`TraxApiDashboardOptions.GraphQLPath` is the path of the Trax GraphQL endpoint the app talks to,
on the host's own origin. When it is not set, the app talks to the path `UseTraxGraphQL` mapped,
whether it was called before or after `UseTraxApiDashboard`, or to `/trax/graphql` when the host
maps the endpoint some other way. A value that is not a path on the host, such as an absolute
URL, is refused at startup.

```csharp compile
using Trax.Api.Dashboard.Extensions;

app.UseTraxApiDashboard(dashboard =>
{
    dashboard.RequireRoles("Admin");
    dashboard.GraphQLPath = "/api/graphql";
}, path: "/ops");
```

`UseTraxApiDashboard` throws at startup when `AddTraxGraphQL()` was not called.

## Security headers

Every response carries:

| Header | Value |
|--------|-------|
| `Content-Security-Policy` | `default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self' wss://<host>; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'` (plus `ws://<host>` when the request is not HTTPS) |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `no-referrer` |
| `X-Frame-Options` | `DENY` |

The app runs under that policy as built: no inline script or style element, no `eval`, and its
fonts are files rather than inline data. A `Host` header that is not a plain host name is left out
of `connect-src`.

## Remarks

- The package carries the built app. A build of `Trax.Api.Dashboard` from the Trax repository
  embeds it only when `Trax.Api/src/Trax.Api.Dashboard/ClientApp` has its npm packages installed,
  and `UseTraxApiDashboard` refuses to start with a build that has none, saying so.
- The footer shows the Trax version the API host runs, read from the GraphQL endpoint.
