---
layout: default
title: AddTraxDashboard
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 1
---

# AddTraxDashboard

Registers Trax.Core Dashboard services including Blazor/Radzen components, train discovery, theme state, and local storage.

## Signatures

### Recommended: WebApplicationBuilder overload

```csharp
public static WebApplicationBuilder AddTraxDashboard(
    this WebApplicationBuilder builder,
    Action<DashboardOptions>? configure = null
)
```

This overload automatically calls `UseStaticWebAssets()` in non-Development environments, ensuring CSS/JS assets from NuGet packages are served correctly.

### IServiceCollection overload

```csharp
public static IServiceCollection AddTraxDashboard(
    this IServiceCollection services,
    Action<DashboardOptions>? configure = null
)
```

When using this overload, you must manually call `builder.WebHost.UseStaticWebAssets()` for non-Development environments.

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `configure` | `Action<DashboardOptions>?` | No | `null` | Optional callback to configure [DashboardOptions]({{ site.baseurl }}{% link sdk-reference/dashboard-api/dashboard-options.md %}) |

## Returns

- **WebApplicationBuilder overload**: `WebApplicationBuilder` — for continued chaining.
- **IServiceCollection overload**: `IServiceCollection` — for continued chaining.

## Example

```csharp
var builder = WebApplication.CreateBuilder(args);

// Recommended approach
builder.AddTraxDashboard(options =>
{
    options.Title = "My App Dashboard";
});

var app = builder.Build();
app.UseTraxDashboard(routePrefix: "/admin/trax");
```

## What It Registers

- `ITrainDiscoveryService` (singleton, from Mediator) — scans DI container for registered trains
- `ILocalStorageService` (scoped) — browser local storage access for theme preferences
- `IThemeStateService` (scoped) — dark/light theme state management
- `IDashboardSettingsService` (scoped) — dashboard configuration access
- Radzen components (via `AddRadzenComponents()`)
- Blazor Interactive Server components (via `AddRazorComponents().AddInteractiveServerComponents()`)

## Prerequisites

`AddTraxDashboard` performs a runtime check that `AddTrax()` was called first. If the `TraxMarker` singleton is not found in the DI container, `AddTraxDashboard` throws `InvalidOperationException`:

```
InvalidOperationException: AddTrax() must be called before AddTraxDashboard().
Call services.AddTrax(...) in your service configuration before calling AddTraxDashboard().
```

This ensures the effect system and its services are available before the dashboard attempts to use them.

## Package

```
dotnet add package Trax.Dashboard
```
