---
layout: default
title: UseTraxDashboard
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 2
---

# UseTraxDashboard

Maps the Trax.Core Dashboard Blazor components at the configured route prefix. Call this after `app.Build()` during application startup.

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
| `routePrefix` | `string` | No | `"/trax"` | URL prefix where the dashboard is mounted. Leading/trailing slashes are normalized. |
| `title` | `string?` | No | `null` | Overrides the dashboard title. `null` keeps the title from [DashboardOptions](/docs/sdk-reference/dashboard-api/dashboard-options). |

## Returns

`WebApplication`, for continued middleware chaining.

## Example

```csharp
var app = builder.Build();

app.UseTraxDashboard(
    routePrefix: "/admin/trains",
    title: "Order Processing Dashboard");

app.Run();
```

The dashboard will be accessible at `https://yourapp/admin/trains`.

## What It Configures

1. `UseStaticFiles()`: serves static assets (CSS, JS)
2. `UseAntiforgery()`: CSRF protection for Blazor forms
3. `MapStaticAssets()`: maps static web assets from the dashboard RCL
4. `MapRazorComponents<App>().AddInteractiveServerRenderMode()`: maps Blazor components with Interactive Server rendering

## Remarks

- Must be called **after** `builder.Build()` and **before** `app.Run()`.
- The `routePrefix` is normalized: `"trax"`, `"/trax"`, and `"/trax/"` all resolve to `"/trax"`.
- The dashboard requires a data provider ([UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) or [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect)) to be configured for metadata and manifest pages to function.
