---
layout: default
title: AddTraxDashboard
description: "Reference for AddTraxDashboard: the WebApplicationBuilder and IServiceCollection overloads, static web assets, what it registers and its prerequisites."
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 1
---

# AddTraxDashboard

Registers the Trax Dashboard services: Blazor and Radzen components, the dashboard settings, theme state and local storage, and the in-circuit authorization check. It needs `AddTrax(...)` with `AddMediator()` and `AddScheduler()` in the same host.

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
| `configure` | `Action<DashboardOptions>?` | No | `null` | Optional callback to configure [DashboardOptions](/docs/sdk-reference/dashboard-api/dashboard-options) |

## Returns

- **WebApplicationBuilder overload**: `WebApplicationBuilder`, for continued chaining.
- **IServiceCollection overload**: `IServiceCollection`, for continued chaining.

## Example

```csharp
var builder = WebApplication.CreateBuilder(args);

// Recommended approach
builder.AddTraxDashboard(options =>
{
    options.Title = "My App Dashboard";
    options.RequirePolicy("TraxAdmin"); // required: who may use the dashboard
});

var app = builder.Build();
app.UseTraxDashboard();  // the dashboard is served at /trax
```

## What It Registers

- `IDashboardSettingsService` (scoped): dashboard configuration access
- A scoped per-circuit authorization check and a `CircuitHandler` that re-check the authorization posture inside an open dashboard (see [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard#remarks)). No `AuthenticationStateProvider` is registered: the check reads the host's.
- Internal scoped services for browser local storage and the dark/light theme state. They are implementation details of the dashboard's own components and cannot be resolved by type from application code.
- Radzen components (via `AddRadzenComponents()`)
- Blazor Interactive Server components (via `AddRazorComponents().AddInteractiveServerComponents()`)
- A post-configure step on `LoggerFilterOptions`, with its own change token, that applies the log levels saved on the Server Settings page. Both overloads register it, so a saved level applies over every configuration source and survives a reload of the host's configuration. Neither overload changes the host's `IConfiguration`.

## Prerequisites

`AddTraxDashboard` performs a runtime check that `AddTrax()` was called first. If the `TraxMarker` singleton is not found in the DI container, `AddTraxDashboard` throws `InvalidOperationException`:

```
InvalidOperationException: AddTraxDashboard() requires AddTrax() to be called first. Call services.AddTrax(trax => ...) before services.AddTraxDashboard().
```

This makes sure the effect system and its services are available before the dashboard attempts to use them.

**Call it once.** A second call on the same service collection throws `InvalidOperationException`
instead of registering a second `DashboardOptions`, because the last registration would win and a
shared bootstrap could replace the host's posture with `AllowAnonymousDashboard()`. Put every
dashboard option in one call.

It registers no train discovery of its own: `ITrainDiscoveryService` comes from `AddMediator()`, which `AddScheduler()` needs anyway. The Scheduler is also required, but it is checked later, by
[UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard), against the built
provider.

## Package

```
dotnet add package Trax.Dashboard
```
