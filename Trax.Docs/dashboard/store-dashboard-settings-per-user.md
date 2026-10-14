---
title: Store dashboard settings per user
description: "Replace the dashboard's localStorage preferences with your own IDashboardSettingsService, for example one that keeps them per user in your database."
type: how-to
area: dashboard
nav_order: 20
---

# Store dashboard settings per user

The dashboard keeps its preferences (polling interval, hidden admin trains, home page panels) in
the browser's `localStorage` through [IDashboardSettingsService](/docs/sdk-reference/dashboard-api/i-dashboard-settings-service).
To keep them somewhere else, implement the interface and register it.

1. Implement `IDashboardSettingsService`. Keep its state per instance: one instance serves one
   circuit (one browser tab). `InitializeAsync()` is called by every component that reads the
   settings, so make repeated calls cheap.
2. Register it **after** `AddTraxDashboard`, as a scoped service. `AddTraxDashboard` uses
   `AddScoped`, so the later registration wins:

```csharp compile
using Trax.Dashboard.Extensions;
using Trax.Dashboard.Services.DashboardSettings;

builder.AddTraxDashboard(o => o.RequireRoles("Admin"));
builder.Services.AddScoped<IDashboardSettingsService, PerUserDashboardSettings>();
```

## SDK Reference

> [IDashboardSettingsService](/docs/sdk-reference/dashboard-api/i-dashboard-settings-service) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard)
