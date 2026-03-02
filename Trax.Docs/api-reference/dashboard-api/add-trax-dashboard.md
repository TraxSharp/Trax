---
layout: default
title: AddTrax.CoreDashboard
parent: Dashboard API
grand_parent: API Reference
nav_order: 1
---

# AddTrax.CoreDashboard

Registers Trax.Core Dashboard services including Blazor/Radzen components, train discovery, theme state, and local storage.

## Signatures

### Recommended: WebApplicationBuilder overload

```csharp
public static WebApplicationBuilder AddTrax.CoreDashboard(
    this WebApplicationBuilder builder,
    Action<DashboardOptions>? configure = null
)
```

This overload automatically calls `UseStaticWebAssets()` in non-Development environments, ensuring CSS/JS assets from NuGet packages are served correctly.

### IServiceCollection overload

```csharp
public static IServiceCollection AddTrax.CoreDashboard(
    this IServiceCollection services,
    Action<DashboardOptions>? configure = null
)
```

When using this overload, you must manually call `builder.WebHost.UseStaticWebAssets()` for non-Development environments.

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `configure` | `Action<DashboardOptions>?` | No | `null` | Optional callback to configure [DashboardOptions]({{ site.baseurl }}{% link api-reference/dashboard-api/dashboard-options.md %}) |

## Returns

- **WebApplicationBuilder overload**: `WebApplicationBuilder` — for continued chaining.
- **IServiceCollection overload**: `IServiceCollection` — for continued chaining.

## Example

```csharp
var builder = WebApplication.CreateBuilder(args);

// Recommended approach
builder.AddTrax.CoreDashboard(options =>
{
    options.Title = "My App Dashboard";
});

var app = builder.Build();
app.UseTrax.CoreDashboard(routePrefix: "/admin/trax");
```

## What It Registers

- `ITrainDiscoveryService` (scoped) — scans DI container for registered trains
- `ILocalStorageService` (scoped) — browser local storage access for theme preferences
- `IThemeStateService` (scoped) — dark/light theme state management
- `IDashboardSettingsService` (scoped) — dashboard configuration access
- Radzen components (via `AddRadzenComponents()`)
- Blazor Interactive Server components (via `AddRazorComponents().AddInteractiveServerComponents()`)

## Package

```
dotnet add package Trax.Dashboard
```
