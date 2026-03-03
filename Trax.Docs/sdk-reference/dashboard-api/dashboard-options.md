---
layout: default
title: DashboardOptions
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 3
---

# DashboardOptions

Configuration class for the Trax.Core Dashboard. Passed via the `configure` callback in [AddTrax.CoreDashboard]({{ site.baseurl }}{% link sdk-reference/dashboard-api/add-trax-dashboard.md %}).

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RoutePrefix` | `string` | `"/trax"` | URL prefix where the dashboard is mounted. Set automatically by [UseTrax.CoreDashboard]({{ site.baseurl }}{% link sdk-reference/dashboard-api/use-trax-dashboard.md %}). |
| `Title` | `string` | `"Trax.Core"` | Title displayed in the dashboard header and browser tab. |
| `EnvironmentName` | `string` | `""` | The hosting environment name (e.g., "Development", "Production"). Auto-populated by `UseTrax.CoreDashboard`. |

## Example

```csharp
builder.AddTrax.CoreDashboard(options =>
{
    options.Title = "My Application - Trains";
});

app.UseTrax.CoreDashboard(routePrefix: "/trains");
// RoutePrefix is set to "/trains" by UseTrax.CoreDashboard
// EnvironmentName is set automatically from the hosting environment
```

## Remarks

- `RoutePrefix` and `EnvironmentName` are typically set by `UseTrax.CoreDashboard`, not in the `configure` callback. Setting them in `configure` will be overwritten.
- `Title` is the only property typically set by users in the `configure` callback.
