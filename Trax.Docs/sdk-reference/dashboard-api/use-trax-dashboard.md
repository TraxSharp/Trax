---
layout: default
title: UseTraxDashboard
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 2
---

# UseTraxDashboard

Maps the Trax Dashboard Blazor components and serves them at `/trax`. Call this after
`app.Build()` during application startup.

**The mount path is fixed.** Every dashboard page carries a compile-time `@page "/trax/..."`
route template, and `MapRazorComponents<App>()` applies no prefix, so the pages are reachable
at `/trax` and nowhere else. `routePrefix` does not move them.

## Signature

```csharp
public static WebApplication UseTraxDashboard(
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

`WebApplication`, for continued middleware chaining.

## Example

```csharp
var app = builder.Build();

app.UseTraxDashboard(title: "Order Processing Dashboard");

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
4. `MapRazorComponents<App>().AddInteractiveServerRenderMode()`: maps Blazor components with Interactive Server rendering

## Remarks

- Must be called **after** `builder.Build()` and **before** `app.Run()`.
- The `routePrefix` is normalized: `"trax"`, `"/trax"`, and `"/trax/"` all resolve to `"/trax"`.
  It is written to `DashboardOptions.RoutePrefix`, which is read in exactly one place,
  `DashboardSidebar`, to build the navigation links.
- The dashboard applies no authorization of its own: there is no `[Authorize]`, no
  `AuthorizeRouteView` and no `RequireAuthorization` anywhere in Trax.Dashboard. Whatever the
  host applies to the `/trax` path is the only gate, and a host that secures its own endpoints
  with per-endpoint `[Authorize]` attributes has applied nothing here. Gate the path
  explicitly if the dashboard should not be public.
- The dashboard requires a data provider ([UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) or [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect)) to be configured for metadata and manifest pages to function.
