---
title: Add the console to a host
description: "Serve the Trax operations console from a Trax.Api host: the package, the operations namespace and its gate, who may load the page, and common failures."
type: how-to
area: dashboard
nav_order: 1
source:
  - Trax.Api/src/Trax.Api.Dashboard/Extensions/ApiDashboardEndpointExtensions.cs
---

# Add the console to a host

The console is served by a host that already serves the Trax GraphQL API. What each page shows is in
[Operations console](/docs/dashboard); every option below is on
[UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard).

## 1. Install the package

```bash
dotnet add package Trax.Api.Dashboard
```

Or in the `.csproj`:

```xml
<PackageReference Include="Trax.Api.Dashboard" Version="1.61.0" />
```

The package carries the built app, so the host needs no Node.

## 2. Register the scheduler, expose the operations namespace, and gate it

Every page reads the GraphQL `operations` fields, which call the scheduler's operations service, so
the host registers the scheduler even if it schedules nothing. `ExposeOperationQueries()` and
`ExposeOperationMutations()` add the namespace; the host refuses to start until it is gated.

```csharp compile
using Microsoft.AspNetCore.Authentication.Cookies;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler()
);

builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie();
builder.Services.AddAuthorization();

builder.Services.AddTraxGraphQL(graphql =>
    graphql.ExposeOperationQueries().ExposeOperationMutations().GateOperations(roles: "Admin")
);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.RequireRoles("Admin").HostAuthenticates());

app.Run();
```

- `GateOperations` decides who may read and act through the console. Treat whoever it admits as an
  operator: they can trigger any manifest and requeue any dead letter. Running, queueing, requeueing
  or resuming a run also checks the train's own `[TraxAuthorize]`, as
  [Where Trax checks authorization](/docs/security/where-authorization-is-checked#the-operations-surface-and-the-dashboard)
  explains.
- `UseTraxApiDashboard` serves the app at `/trax/dashboard`. Pass `path:` to move it.

## 3. Choose who may load the page

`UseTraxApiDashboard` refuses to start until its options name a posture. The posture gates the page
and its scripts; the data is gated by step 2 whatever is chosen here.

| Posture | When |
|---|---|
| `RequireRoles(...)` or `RequirePolicy(...)`, with `HostAuthenticates()` | The browser is signed in to the host (a cookie or OpenID Connect sign-in, or a proxy that adds identity). The console asks for no key and the GraphQL endpoint authorizes each request by the host's scheme. This is step 2's example. |
| `RequireRoles(...)` or `RequirePolicy(...)` | The browser is signed in for the page, and the operator pastes an API key or bearer token into the console for its GraphQL requests. |
| `AllowAnonymousShell()` | The host authenticates only by API key or bearer token, which a page load cannot carry. Anyone can load the page; the key the operator pastes is what the GraphQL gate checks. A warning is logged on every start. |

A pasted key is held in the page's memory only, never in browser storage, so it is asked for again
after every reload.

## 4. Open it

Run the host and go to `/trax/dashboard`. The Overview opens; the sidebar's entries are described
in [Operations console](/docs/dashboard#how-it-is-organised). Every registered train is under
**Trains**.

## Moving from the Blazor dashboard

The Blazor dashboard (`Trax.Dashboard`, `AddTraxDashboard` and `UseTraxDashboard`) is gone; the
console replaces it.

1. Replace the `Trax.Dashboard` package with `Trax.Api.Dashboard`, and delete
   `<RequiresAspNetWebAssets>` from the csproj if nothing else needs it.
2. Delete the `AddTraxDashboard(...)` and `UseTraxDashboard()` calls. A custom
   `IDashboardSettingsService` goes with them: the console keeps its preferences in the browser.
3. Add `AddTraxGraphQL` with the operations namespace and its gate, and `UseTraxApiDashboard`, as in
   step 2. The roles or policy the Blazor posture named become the `GateOperations` and
   `UseTraxApiDashboard` arguments.
4. Links to `/trax/...` pages change: the console is at `/trax/dashboard`, and its pages are named
   for what they hold (`/runs/42` for a run, `/schedules/7` for a manifest).

## Troubleshooting

### The host refuses to start: the operations namespace is not exposed

Add `ExposeOperationQueries()`, and `ExposeOperationMutations()` for the console's actions, to
`AddTraxGraphQL`.

### The host refuses to start: no posture

Name one in the `UseTraxApiDashboard` options; see step 3.

### Every page load is refused with 401

The posture's scheme is an API key or a bearer token, which a page load never carries. Use a
cookie or OpenID Connect sign-in for the page, or `AllowAnonymousShell()`.

### The page loads, then every read fails

The caller does not meet the `GateOperations` gate. With `HostAuthenticates()`, the host's scheme
must be one the GraphQL endpoint accepts; without it, the key the operator pasted must belong to a
caller the gate admits.

### No trains listed

The trains' assembly was not passed to `AddMediator`, or the train's interface does not extend
`IServiceTrain<TIn, TOut>`. Discovery reads the host's registrations through
[ITrainDiscoveryService](/docs/sdk-reference/mediator-api/i-train-discovery-service).

## SDK Reference

> [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/i-train-discovery-service)
