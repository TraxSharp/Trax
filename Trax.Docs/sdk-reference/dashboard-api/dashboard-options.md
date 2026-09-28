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
| `RoutePrefix` | `string` | `"/trax"` | Prefix used to build the sidebar navigation links. Overwritten by [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard). It does not move the pages, whose routes are compile-time `/trax/...` templates. |
| `Title` | `string` | `"Trax"` | Title displayed in the dashboard header and browser tab. |
| `EnvironmentName` | `string` | `""` | The hosting environment name (e.g., "Development", "Production"). Auto-populated by `UseTraxDashboard`. |

## Authorization posture

Exactly one posture is required before [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)
will start. Each method returns the options, so they chain.

| Method | Description |
|--------|-------------|
| `RequirePolicy(string policy)` | Every dashboard endpoint requires the named policy. Throws `ArgumentException` for a blank name. |
| `RequireRoles(params string[] roles)` | Every dashboard endpoint requires one of the roles. Throws `ArgumentException` for no roles or a blank one. May be combined with `RequirePolicy`. |
| `AllowAnonymousDashboard()` | The dashboard adds no authorization of its own; `UseTraxDashboard` logs a warning on every start. Throws `InvalidOperationException` when combined with either of the above. |

## Example

```csharp
builder.AddTraxDashboard(options =>
{
    options.Title = "My Application - Trains";
    options.RequireRoles("Admin");
});

app.UseTraxDashboard();
// RoutePrefix stays at "/trax", which is where the pages are
// EnvironmentName is set automatically from the hosting environment
```

## Remarks

- `RoutePrefix` and `EnvironmentName` are set by `UseTraxDashboard`, not in the `configure` callback. Setting them in `configure` will be overwritten.
- `RoutePrefix` is read only by the sidebar. Changing it from `/trax` leaves the pages where they are and points every navigation link somewhere that does not exist.
- `Title` is the only property worth setting in the `configure` callback, alongside the posture.
