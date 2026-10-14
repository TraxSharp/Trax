---
title: Add the dashboard to a host
description: "Add Trax.Dashboard to an ASP.NET Core host: the package, the csproj property it needs, the scheduler, an authorization posture, and fixes for common failures."
type: how-to
area: dashboard
nav_order: 1
source:
  - Trax.Dashboard/src/Trax.Dashboard/Extensions/DashboardServiceExtensions.cs
  - Trax.Dashboard/src/Trax.Dashboard/Configuration/DashboardOptions.cs
---

# Add the dashboard to a host

Trax.Dashboard is a Blazor Server UI, mounted at `/trax`, for inspecting trains and runs, managing
manifests and dead letters, and changing scheduler and effect settings at runtime. What each page
shows and does is in [Dashboard pages](/docs/dashboard/pages).

For the same dashboard as a single-page app that a Trax.Api host serves over its GraphQL endpoint, see
[API Dashboard](/docs/sdk-reference/api-dashboard).

## 1. Install the package

```bash
dotnet add package Trax.Dashboard
```

Or in the `.csproj`:

```xml
<PackageReference Include="Trax.Dashboard" Version="1.61.0" />
```

Pin an exact version, the release these docs are checked against or a newer one, rather than a
floating `1.*`, which restores whatever was published last.

## 2. Set RequiresAspNetWebAssets

Every host that calls `UseTraxDashboard()` sets this in its csproj:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

Without it the build leaves out `_framework/blazor.web.js`, the Blazor circuit never connects, and
the dashboard renders but does nothing when clicked. The package cannot set it for you: the Razor
SDK overwrites the package's `.props` file with static-asset imports when it packs, and a `.targets`
file is read after the SDK has already read the property.

After building, check it took effect:

```bash
curl -I http://localhost:5000/trax/_framework/blazor.web.js
# HTTP/1.1 200 OK   the property is set
# HTTP/1.1 404      the property is missing; set it and rebuild
```

## 3. Register the scheduler, authentication and the dashboard

The dashboard's pages queue, run and cancel trains and change settings through the scheduler's
`IOperationsService`, the service the GraphQL `operations` fields call, so the host registers the
scheduler even if it schedules nothing.

```csharp compile
using Microsoft.AspNetCore.Authentication.Cookies;
using Trax.Dashboard.Extensions;
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

builder.AddTraxDashboard(dashboard => dashboard.RequireRoles("Admin"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxDashboard();

app.Run();
```

- Call `AddTraxDashboard` on the `WebApplicationBuilder`, after `AddTrax`, once. This overload also
  serves the dashboard's static files outside Development; the `IServiceCollection` overload does
  not.
- Call `UseTraxDashboard()` after `builder.Build()` and before `app.Run()`. It adds
  `UseStaticFiles()` and `UseAntiforgery()` to the pipeline and maps the pages.
- The pages are at `/trax`, and the path cannot be moved. The `routePrefix` argument only changes
  the sidebar's links, so leave it at its default.

## 4. Choose who may use it

The dashboard refuses to start until its options name a posture:

| Posture | When |
|---|---|
| `RequireRoles("<role>", ...)` | Operators have a role in the host's authentication. |
| `RequirePolicy("<policy>")` | The rule is more than a role. The policy must be registered with `AddAuthorization`. |
| `AllowAnonymousDashboard()` | Something in front of `/trax`, a fallback policy or an ingress rule, is the gate, or for local development. A warning is logged on every start. |

`RequireRoles` and `RequirePolicy` also need an authentication scheme to challenge a refused caller
with, or the host refuses to start; that is why step 3 registers one. Every check, and its message,
is on [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard).

Treat whoever the posture admits as an administrator. Queueing and running a train from the
dashboard skip the train's own `[TraxAuthorize]` requirements, so anyone who reaches the dashboard
can run any train; [Where Trax checks authorization](/docs/security/where-authorization-is-checked)
explains why.

An open dashboard re-checks the posture against the host's `AuthenticationStateProvider` when the
provider reports a change and every minute, and checks every click against the latest result. It
registers no provider of its own, and ASP.NET Core's default keeps the user the connection arrived
with. For a revoked role or a sign-out to close an open dashboard, register a revalidating
provider (ASP.NET Identity's template does), or use a policy whose handlers read live state. The
in-dashboard check has no `HttpContext`, so a handler that reads it gets `null`.

## 5. Open it

Run the host and go to `/trax/trains`: every train registered through `AddMediator` is listed.
The other pages are under **Data** and **Settings** in the sidebar; see
[Dashboard pages](/docs/dashboard/pages).

## Optional: persisted operations and dead letters

- With persisted operations on the host, the dashboard adds a **Persisted Operations** page. Its
  setup, including a dashboard that runs in a process of its own, is in
  [Turn on persisted operations](/docs/api/turn-on-persisted-operations#7-manage-operations-from-the-dashboard).
- Dead letters are requeued or acknowledged from the **Dead Letters** page; the steps are in
  [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup).

The dashboard runs alongside an existing Blazor Server app's `AddRazorComponents()`, with its own
layout. A minimal API or MVC host gets the Blazor Server services from `AddTraxDashboard()`.

## Troubleshooting

### The page loads but nothing responds to clicks

`RequiresAspNetWebAssets` is missing from the csproj; see step 2.

### Blank page, or the dashboard's URLs return 404

`UseTraxDashboard()` was not called, or was called before `builder.Build()`.

### Styles missing, `_content/` paths return 404

Outside Development, register with `builder.AddTraxDashboard(...)` rather than
`builder.Services.AddTraxDashboard(...)`, or call `builder.WebHost.UseStaticWebAssets()` yourself.
If it still fails, something earlier in the pipeline is short-circuiting requests before the static
file middleware `UseTraxDashboard()` adds.

### "UseTraxDashboard() requires the Trax Scheduler"

Add `AddScheduler()` after `AddMediator(...)` inside `AddTrax(...)`.

### "UseTraxDashboard() needs to know who may use the dashboard"

Name a posture in the `AddTraxDashboard` options; see step 4.

### "The dashboard requires authorization, but the host has no authentication scheme"

A role or policy posture needs a scheme to challenge with. Register one with
`AddAuthentication(...)`, set as the default when there is more than one, or name it on the policy
with `AddAuthenticationSchemes(...)`.

### "AddTraxDashboard() requires AddTrax() to be called first"

Move `AddTraxDashboard` after `AddTrax(...)`.

### "AddTraxDashboard() has already been called on this host"

Two calls reached the same service collection, often one in a shared bootstrap and one in the
host. Keep one, with every option in it: a second would have replaced the first one's posture.

### No trains listed

The trains' assembly was not passed to `AddMediator`, or the train's interface does not extend
`IServiceTrain<TIn, TOut>`. Discovery reads the host's registrations through
[ITrainDiscoveryService](/docs/sdk-reference/mediator-api/i-train-discovery-service); a class registered
only through a factory cannot be read and is not listed.

## SDK Reference

> [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [DashboardOptions](/docs/sdk-reference/dashboard-api/dashboard-options) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/i-train-discovery-service)
