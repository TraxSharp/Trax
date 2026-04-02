---
layout: default
title: Dashboard API
parent: SDK Reference
nav_order: 5
has_children: true
---

# Dashboard API

The Trax.Core dashboard is a Blazor Server UI that provides real-time visibility into train execution, metadata, manifests, dead letters, and effect provider configuration. It's distributed as a Razor Class Library and mounted into your existing ASP.NET Core application.

Effect providers that implement `IConfigurableEffectProviderFactory<TConfiguration>` (or `IConfigurableJunctionEffectProviderFactory<TConfiguration>`) expose a settings button on the dashboard's **Effects** page, where their configuration properties can be modified at runtime.

```csharp
// In Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
);

builder.AddTraxDashboard();  // Requires AddTrax() first; throws InvalidOperationException otherwise

var app = builder.Build();
app.UseTraxDashboard();
```

| Page | Description |
|------|-------------|
| [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | Registers dashboard services (Blazor, Radzen, train discovery) |
| [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | Maps the dashboard Blazor components at a route prefix |
| [DashboardOptions](/docs/sdk-reference/dashboard-api/dashboard-options) | Configuration options for route prefix, title, and environment |
