---
layout: default
title: IDashboardSettingsService
description: Reference for IDashboardSettingsService, the per-browser dashboard preferences kept in localStorage, its members and how to replace it.
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 4
---

# IDashboardSettingsService

Per-browser dashboard preferences (the polling interval, whether the scheduler's own trains are hidden, which home page panels show) and the outcome of the most recent data refresh. The dashboard's pages read it; the **User Settings** page writes it.

`AddTraxDashboard` registers the default implementation as a scoped service, so in Blazor Server there is one per circuit (browser tab). It keeps every preference in the browser's `localStorage`, not on the server. Replace it to store preferences elsewhere, for example per user in your database.

## Signature

```csharp
namespace Trax.Dashboard.Services.DashboardSettings;

public interface IDashboardSettingsService
{
    TimeSpan PollingInterval { get; }
    DateTime LastPollTime { get; }
    bool HideAdminTrains { get; }
    string? LastPollError => null;

    bool ShowSummaryCards { get; }
    bool ShowExecutionsChart { get; }
    bool ShowFailures { get; }
    bool ShowAvgDuration { get; }
    bool ShowServerHealth { get; }

    Task InitializeAsync();
    Task SetPollingIntervalAsync(int seconds);
    Task SetHideAdminTrainsAsync(bool hide);
    Task SetComponentVisibilityAsync(string key, bool visible);
    void NotifyPolled();
    void NotifyPollFailed(string message) { }
}
```

## Members

| Member | Default | Description |
|--------|---------|-------------|
| `PollingInterval` | 5 seconds | How long pages wait between refreshes. Read on every tick, so a change applies on the next one. |
| `LastPollTime` | | UTC time of the last completed load on any polling page in this circuit. The header draws its countdown from it. |
| `HideAdminTrains` | `true` | Leaves the scheduler's own trains (manifest manager, job dispatcher, job runner, the cleanup trains) out of the home page metrics, the trains list and the manifest and metadata lists |
| `LastPollError` | `null` | The message of the most recent failed refresh, cleared when one succeeds. Only background poll ticks report here. The interface's default implementation always returns `null`. |
| `ShowSummaryCards`, `ShowExecutionsChart`, `ShowFailures`, `ShowAvgDuration`, `ShowServerHealth` | `true` | Which home page panels show |
| `InitializeAsync()` | | Loads the stored preferences. Every component that reads them calls it first, so repeated calls must be cheap; until it runs, the properties hold their defaults. |
| `SetPollingIntervalAsync(int seconds)` | | Sets and persists `PollingInterval`. Values below 1 are raised to 1. |
| `SetHideAdminTrainsAsync(bool hide)` | | Sets and persists `HideAdminTrains` |
| `SetComponentVisibilityAsync(string key, bool visible)` | | Shows or hides one home page panel and persists the choice. The dashboard passes its storage key for the panel: `trax-show-summary-cards`, `trax-show-executions-chart`, `trax-show-failures`, `trax-show-avg-duration` or `trax-show-server-health`. The default implementation writes an unrecognised key to storage but changes no property. |
| `NotifyPolled()` | | Called by a page after a load completes: sets `LastPollTime` to now and clears `LastPollError` |
| `NotifyPollFailed(string message)` | | Called when a refresh fails. The interface's default implementation discards the message. |

## Replacing it

Register your implementation **after** `AddTraxDashboard`; it uses `AddScoped`, so the later registration wins. Register it scoped, because each circuit holds its own state.

```csharp
builder.AddTraxDashboard(o => o.RequireRoles("Admin"));
builder.Services.AddScoped<IDashboardSettingsService, PerUserDashboardSettings>();
```

## Package

```
dotnet add package Trax.Dashboard
```
