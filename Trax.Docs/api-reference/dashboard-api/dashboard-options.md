---
layout: default
title: DashboardOptions
parent: Dashboard API
grand_parent: API Reference
nav_order: 3
---

# DashboardOptions

Configuration class for the Trax.Core Dashboard. Passed via the `configure` callback in [AddTrax.CoreDashboard]({{ site.baseurl }}{% link api-reference/dashboard-api/add-trax-dashboard.md %}).

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RoutePrefix` | `string` | `"/trax"` | URL prefix where the dashboard is mounted. Set automatically by [UseTrax.CoreDashboard]({{ site.baseurl }}{% link api-reference/dashboard-api/use-trax-dashboard.md %}). |
| `Title` | `string` | `"Trax.Core"` | Title displayed in the dashboard header and browser tab. |
| `EnvironmentName` | `string` | `""` | The hosting environment name (e.g., "Development", "Production"). Auto-populated by `UseTrax.CoreDashboard`. |

## Example

```csharp
builder.AddTrax.CoreDashboard(options =>
{
    options.Title = "My Application - Workflows";
});

app.UseTrax.CoreDashboard(routePrefix: "/workflows");
// RoutePrefix is set to "/workflows" by UseTrax.CoreDashboard
// EnvironmentName is set automatically from the hosting environment
```

## Remarks

- `RoutePrefix` and `EnvironmentName` are typically set by `UseTrax.CoreDashboard`, not in the `configure` callback. Setting them in `configure` will be overwritten.
- `Title` is the only property typically set by users in the `configure` callback.
