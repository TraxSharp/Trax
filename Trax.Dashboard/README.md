# Trax.Dashboard

[![NuGet Version](https://img.shields.io/nuget/v/Trax.Dashboard)](https://www.nuget.org/packages/Trax.Dashboard/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Operations control room for [Trax](https://www.nuget.org/packages/Trax.Effect/) — a Blazor Server dashboard for monitoring train journeys, timetables, dead letters, and live station service configuration.

## What This Does

Drop a control room into any ASP.NET Core application that uses Trax. Two lines of code give you a full operations dashboard for watching train journeys in real time, browsing the timetable, inspecting lost shipments, and toggling station services without restarting.

No separate service to deploy. It mounts directly into your existing application as a Blazor Server component.

## Installation

```bash
dotnet add package Trax.Dashboard
```

## Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddTraxDashboard();

builder.Services.AddTraxEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddServiceTrainBus(typeof(Program).Assembly)
);

var app = builder.Build();

app.UseTraxDashboard();    // Mounts at /trax by default

app.Run();
```

Custom route prefix:

```csharp
app.UseTraxDashboard("/my-dashboard");
```

## Pages

### Home

Overview of the whole network — summary cards showing train counts by state, a donut chart of journey outcomes, and a 24-hour departure timeline.

### Trains

The departure board. Grid of all registered `IServiceTrain<,>` implementations discovered in your assemblies — interface type, concrete type, cargo in, cargo out, and DI lifetime. Useful for verifying that your trains are wired up correctly.

### Data

- **Metadata** — the journey log. Execution history for every train run. Filter by state, train name, or time range. Cancel trains in transit directly from the grid.
- **Logs** — application logs captured during train journeys.
- **Manifests** — the timetable. Scheduled train definitions from Trax.Scheduler. View schedule, retry policy, last departure time, and enable/disable individual manifests.
- **Manifest Groups** — fleet-level statistics and dispatch settings (max active trains, priority) per group.
- **Dead Letters** — the lost shipment office. Trains that derailed beyond their retry limit. Inspect the failure details and decide whether to re-dispatch or discard.

### Effects

Toggle individual station services on or off at runtime. Adjust settings like log levels and serialization options without restarting the application.

### User Settings

Per-session preferences — polling interval for live data, visibility toggles for dashboard sections.

## Requirements

Trax.Dashboard is a Razor Class Library built on Blazor Server with [Radzen](https://www.radzen.com/) components. Your host application needs:

- ASP.NET Core (the Web SDK)
- Interactive Server render mode enabled (Blazor Server)

The dashboard handles its own static assets and routing — you don't need to configure Radzen separately.

## Related Packages

| Package | Purpose |
|---------|---------|
| [Trax.Core](https://www.nuget.org/packages/Trax.Core/) | The locomotive — `Train`, steps, railway programming |
| [Trax.Effect](https://www.nuget.org/packages/Trax.Effect/) | `ServiceTrain` with journey logging and station services |
| [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator/) | Dispatch station — route cargo to the right train via `TrainBus` |
| [Trax.Scheduler](https://www.nuget.org/packages/Trax.Scheduler/) | Timetables — recurring trains with retries and dead-lettering |

Full documentation: [traxsharp.github.io/Trax.Docs](https://traxsharp.github.io/Trax.Docs)

## License

MIT
