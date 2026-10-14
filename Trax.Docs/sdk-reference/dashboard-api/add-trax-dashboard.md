---
title: AddTraxDashboard
description: "Reference for AddTraxDashboard: the WebApplicationBuilder and IServiceCollection overloads, static web assets, what it registers and its prerequisites."
type: reference
area: dashboard
package: Trax.Dashboard
namespace: Trax.Dashboard.Extensions
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 1
source:
  - Trax.Dashboard/src/Trax.Dashboard/Extensions/DashboardServiceExtensions.cs
---

# AddTraxDashboard

Registers the Trax Dashboard services: Blazor and Radzen components, the dashboard options and
settings, theme state and local storage, and the in-circuit authorization check. Both overloads
are on `DashboardServiceExtensions`. The host also needs `AddTrax(...)` with `AddMediator()` and
`AddScheduler()`.

## Signatures

```csharp fragment
public static WebApplicationBuilder AddTraxDashboard(
    this WebApplicationBuilder builder,
    Action<DashboardOptions>? configure = null
)

public static IServiceCollection AddTraxDashboard(
    this IServiceCollection services,
    Action<DashboardOptions>? configure = null
)
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `builder` / `services` | `WebApplicationBuilder` / `IServiceCollection` | Yes | | The host, after `AddTrax(...)` |
| `configure` | `Action<DashboardOptions>?` | No | `null` | Sets the title and the authorization posture on [DashboardOptions](/docs/sdk-reference/dashboard-api/dashboard-options). A posture is required by [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard). |

## Returns

The `WebApplicationBuilder` or `IServiceCollection` it was called on.

## Behavior

The `WebApplicationBuilder` overload calls `builder.WebHost.UseStaticWebAssets()` outside the
Development environment, so the CSS and JavaScript the package ships are served, then calls the
`IServiceCollection` overload. A host that uses the `IServiceCollection` overload calls
`UseStaticWebAssets()` itself outside Development.

### Registered services

- `DashboardOptions` (singleton): the options `configure` set
- `IDashboardSettingsService` (scoped): per-circuit dashboard preferences, see [IDashboardSettingsService](/docs/sdk-reference/dashboard-api/i-dashboard-settings-service)
- A scoped per-circuit authorization check and a `CircuitHandler` that re-check the authorization posture inside an open dashboard (see [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard#in-circuit-checks)). No `AuthenticationStateProvider` is registered: the check reads the host's.
- Internal scoped services for browser local storage and the dark/light theme state. They cannot be resolved by type from application code.
- Radzen components (`AddRadzenComponents()`)
- Blazor Interactive Server components (`AddRazorComponents().AddInteractiveServerComponents()`)

Neither overload touches the host's logging or its `IConfiguration`. The log levels saved on the
Server Settings page go through [`ILogLevelService`](/docs/sdk-reference/scheduler-api/i-log-level-service),
which `AddScheduler()` registers.

### Prerequisites

`AddTraxDashboard` performs a runtime check that `AddTrax()` was called first. If the `TraxMarker` singleton is not found in the DI container, `AddTraxDashboard` throws `InvalidOperationException`:

```text
InvalidOperationException: AddTraxDashboard() requires AddTrax() to be called first. Call services.AddTrax(trax => ...) before services.AddTraxDashboard().
```

**Call it once.** A second call on the same service collection throws `InvalidOperationException`
instead of registering a second `DashboardOptions`, because the last registration would win and a
shared bootstrap could replace the host's posture with `AllowAnonymousDashboard()`. Put every
dashboard option in one call.

It registers no train discovery of its own: `ITrainDiscoveryService` comes from `AddMediator()`, which `AddScheduler()` needs anyway. The Scheduler is also required, but it is checked later, by
[UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard), against the built
provider.

## Example

```csharp compile
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler());

builder.AddTraxDashboard(options =>
{
    options.Title = "My App Dashboard";
    options.RequirePolicy("TraxAdmin");
});

var app = builder.Build();
app.UseTraxDashboard(); // served at /trax
app.Run();
```

## See also

- [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)
- [DashboardOptions](/docs/sdk-reference/dashboard-api/dashboard-options)
- [Dashboard](/docs/dashboard)
