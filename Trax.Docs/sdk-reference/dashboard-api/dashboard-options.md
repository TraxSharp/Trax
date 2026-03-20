---
layout: default
title: DashboardOptions
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 3
---

# DashboardOptions

Configuration class for the Trax.Core Dashboard. Passed via the `configure` callback in [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard).

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RoutePrefix` | `string` | `"/trax"` | URL prefix where the dashboard is mounted. Set automatically by [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard). |
| `Title` | `string` | `"Trax"` | Title displayed in the dashboard header and browser tab. |
| `EnvironmentName` | `string` | `""` | The hosting environment name (e.g., "Development", "Production"). Auto-populated by `UseTraxDashboard`. |

## Example

```csharp
builder.AddTraxDashboard(options =>
{
    options.Title = "My Application - Trains";
});

app.UseTraxDashboard(routePrefix: "/trains");
// RoutePrefix is set to "/trains" by UseTraxDashboard
// EnvironmentName is set automatically from the hosting environment
```

## Remarks

- `RoutePrefix` and `EnvironmentName` are typically set by `UseTraxDashboard`, not in the `configure` callback. Setting them in `configure` will be overwritten.
- `Title` is the only property typically set by users in the `configure` callback.
