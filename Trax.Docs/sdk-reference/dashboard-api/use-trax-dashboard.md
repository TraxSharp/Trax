---
layout: default
title: UseTraxDashboard
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 2
---

# UseTraxDashboard

Maps the Trax Dashboard Blazor components and serves them at `/trax`, gated by the
authorization posture chosen in [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard).
Call this after `app.Build()` during application startup.

**It refuses to start without a posture.** The dashboard can queue, run and cancel trains and
change scheduler settings, so the host has to say who may use it, in the `configure` callback
of `AddTraxDashboard`:

| Posture | Effect |
|---------|--------|
| `RequirePolicy("<name>")` | Every dashboard endpoint requires the named policy. The policy must be registered with `AddAuthorization`, or `UseTraxDashboard` throws at startup. |
| `RequireRoles("<role>", ...)` | Every dashboard endpoint requires one of the roles. Combined with `RequirePolicy`, both apply. |
| `AllowAnonymousDashboard()` | The dashboard adds no authorization of its own, and a warning is logged on every start. A fallback policy or an ingress rule in front of it still applies. Use it only when one of those is the gate, or for local development. |

With none of them, `UseTraxDashboard` throws `InvalidOperationException` naming the three.
`AllowAnonymousDashboard()` together with either of the others throws as a contradiction.

**It refuses to start without the Scheduler.** The dashboard's pages queue, run, cancel and
inspect work through `IOperationsService`, which only
[AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) registers. When the built
provider has no `IOperationsService`, `UseTraxDashboard` throws `InvalidOperationException`
naming `AddScheduler()`, rather than mapping pages that fail on their first request.

"Any authenticated user" is not a posture of its own. On a host with public sign-up it is the
same as anonymous. If that is what you mean, register a policy that says so and name it:

```csharp
builder.Services.AddAuthorization(o =>
    o.AddPolicy("TraxDashboard", p => p.RequireAuthenticatedUser())
);
builder.AddTraxDashboard(o => o.RequirePolicy("TraxDashboard"));
```

**The mount path is fixed.** Every dashboard page carries a compile-time `@page "/trax/..."`
route template, and `MapRazorComponents<App>()` applies no prefix, so the pages are reachable
at `/trax` and nowhere else. `routePrefix` does not move them.

## Signature

```csharp
public static RazorComponentsEndpointConventionBuilder UseTraxDashboard(
    this WebApplication app,
    string routePrefix = "/trax",
    string? title = null
)
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `routePrefix` | `string` | No | `"/trax"` | The prefix used to build the sidebar navigation links, and nothing else. Leading and trailing slashes are normalized. Leave it at the default. |
| `title` | `string?` | No | `null` | Overrides the dashboard title. `null` keeps the title from [DashboardOptions](/docs/sdk-reference/dashboard-api/dashboard-options). |

## Returns

The `RazorComponentsEndpointConventionBuilder` for the dashboard's endpoints: its pages and
the Blazor circuit hub (`/_blazor`). Conventions added to it, such as `RequireHost` or a rate
limiter, apply to all of them on top of the posture; they do not replace it. The return type
was `WebApplication` before the posture was required, so code that chained middleware calls
onto it now calls them on `app` instead.

## Example

```csharp
builder.Services.AddAuthorization(o =>
    o.AddPolicy("TraxAdmin", p => p.RequireRole("Admin"))
);
builder.AddTraxDashboard(o => o.RequirePolicy("TraxAdmin"));

var app = builder.Build();

app.UseTraxDashboard(title: "Order Processing Dashboard")
    .RequireHost("admin.example.com");

app.Run();
```

The dashboard is served at `https://yourapp/trax`.

Passing a different `routePrefix` produces a broken dashboard rather than a moved one. The
pages stay at `/trax` while every sidebar link points at the new prefix, so the dashboard is
reachable only by typing `/trax` and every navigation link inside it 404s.

## What It Configures

1. `UseStaticFiles()`: serves static assets (CSS, JS)
2. `UseAntiforgery()`: CSRF protection for Blazor forms
3. `MapStaticAssets()`: maps static web assets from the dashboard RCL
4. `MapRazorComponents<App>().AddInteractiveServerRenderMode()`: maps Blazor components with Interactive Server rendering, and applies the posture to every endpoint it maps

Static assets (`MapStaticAssets()`) are outside the posture. They are the host's whole static
asset manifest, not only the dashboard's files, and gating them would gate the host's own
login page.

## Remarks

- Must be called **after** `builder.Build()` and **before** `app.Run()`.
- The `routePrefix` is normalized: `"trax"`, `"/trax"`, and `"/trax/"` all resolve to `"/trax"`.
  It is written to `DashboardOptions.RoutePrefix`, which is read in exactly one place,
  `DashboardSidebar`, to build the navigation links. This argument is the only way to set it.
- The posture is checked on each page request and on the circuit hub's negotiate and
  connect. Navigation inside an established circuit does not go back through the endpoint, so
  the dashboard also re-checks the posture inside the circuit, against the host's
  `AuthenticationStateProvider`: when the dashboard's root component attaches the circuit,
  whenever the provider reports a change, every minute, and before each persisted-operation
  write. Before every inbound circuit message (a click, a change, an interop call) it reads the
  latest verdict, and once the user is refused it closes the circuit and the page reloads through
  the endpoint. A failure to evaluate the posture (no provider registered, a policy handler that
  throws) is a refusal. With `AllowAnonymousDashboard()` there is nothing to re-check.
- The in-circuit check has three limits. The dashboard registers no
  `AuthenticationStateProvider`, so it sees a revoked role or a sign-out only when the host's
  provider does; ASP.NET Core's default `ServerAuthenticationStateProvider` keeps the user the
  connection arrived with, so a host that wants revocation inside an open dashboard registers a
  revalidating provider or names a policy whose handlers read live state. There is no
  `HttpContext`: a policy handler that reads it as the resource gets `null`, and the policy's
  authentication schemes are not re-run. Only the posture is re-checked: conventions added to the
  returned builder, such as `RequireHost`, apply at the endpoint only.
- Authentication is still the host's. With no scheme that can challenge, a gated request fails
  rather than being served.
- The dashboard requires the Scheduler, which in turn requires a data provider ([UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) or [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect)).
