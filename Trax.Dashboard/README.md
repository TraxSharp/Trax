# Trax.Dashboard

[![Build](https://github.com/TraxSharp/Trax.Dashboard/actions/workflows/nuget_release.yml/badge.svg)](https://github.com/TraxSharp/Trax.Dashboard/actions/workflows/nuget_release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Trax.Dashboard)](https://www.nuget.org/packages/Trax.Dashboard/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trax.Dashboard)](https://www.nuget.org/packages/Trax.Dashboard/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Dashboard/blob/main/LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/TraxSharp/Trax.Dashboard)](https://github.com/TraxSharp/Trax.Dashboard/commits/main)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Dashboard/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Dashboard)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

Operations control room for [Trax](https://www.nuget.org/packages/Trax.Effect/). A Blazor Server dashboard for monitoring train journeys, timetables, dead letters, and live station service configuration.

## The Trax Stack

Trax is a layered framework split across several repos. You can stop at whatever layer solves your problem. **You are here: Trax.Dashboard.**

| Repo | Adds |
|------|------|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Pipelines, junctions, railway error propagation |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | Execution logging, DI, pluggable storage |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | Decoupled dispatch via `TrainBus` |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron schedules, retries, dead-letter queues |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL API for remote access |
| **[Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard)** | Blazor monitoring UI |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | `trax-cli` project scaffolding tool |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Sample apps and a `dotnet new` template |

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs).

## What This Does

Drop a control room into any ASP.NET Core application that uses Trax. Two lines of code give you a full operations dashboard for watching train journeys in real time, browsing the timetable, inspecting lost shipments, and toggling station services without restarting.

No separate service to deploy. It mounts directly into your existing application as a Blazor Server component.

## Installation

Install it in the ASP.NET Core application that already hosts Trax. The example below also uses the
Postgres data provider:

```bash
dotnet add package Trax.Dashboard
dotnet add package Trax.Effect.Data.Postgres
```

## Setup

```csharp
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
);

builder.AddTraxDashboard();    // must follow AddTrax; it checks for it and throws otherwise

var app = builder.Build();

app.UseTraxDashboard();    // Mounts at /trax

app.Run();
```

The host's csproj must also opt into the framework's Blazor web assets, or `_framework/blazor.web.js`
is missing and the dashboard renders but ignores every click:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

Use `builder.AddTraxDashboard()` on the `WebApplicationBuilder` rather than
`builder.Services.AddTraxDashboard()`: only the builder overload calls
`builder.WebHost.UseStaticWebAssets()` outside Development, which the dashboard's CSS and JS need in
every environment.

The mount path is fixed. Every page carries a compile-time `@page "/trax/..."` route, so the
route prefix argument to `UseTraxDashboard()` only changes the links the sidebar builds and
leaves the pages where they are. Passing one gives you navigation that points at nothing.

The dashboard applies no authorization of its own. It is served wherever you mount it, gated
by whatever your application already applies to that path: everything if you have registered
a fallback authorization policy, nothing if your `[Authorize]` attributes sit on your own
controllers and pages. Put a fallback policy or path-scoped middleware in front of `/trax`
before you expose the host.

## Pages

### Home

Overview of the whole network. Summary cards showing train counts by state, a donut chart of journey outcomes, and a 24-hour departure timeline.

### Trains

The departure board. Grid of all registered `IServiceTrain<,>` implementations discovered in your assemblies: interface type, concrete type, cargo in, cargo out, and DI lifetime. Useful for verifying that your trains are wired up correctly.

### Data

- **Metadata**: the journey log. Execution history for every train run. Filter by state, train name, or time range. Cancel trains in transit directly from the grid.
- **Logs**: application logs captured during train journeys.
- **Manifests**: the timetable. Scheduled train definitions from Trax.Scheduler. View schedule, retry policy, last departure time, and enable/disable individual manifests.
- **Manifest Groups**: fleet-level statistics and dispatch settings (max active trains, priority) per group.
- **Dead Letters**: the lost shipment office. Trains that derailed beyond their retry limit. Inspect the failure details and decide whether to re-dispatch or discard.
- **Work Queue**: runs waiting to be dispatched, including staged entries. Cancel pending entries individually or in bulk.
- **Persisted Operations**: the GraphQL persisted-operation documents the API accepts. Upload, edit and deactivate them.

### Effects

Toggle individual station services on or off at runtime. Adjust settings like log levels and serialization options without restarting the application.

### Server Settings

Scheduler settings at runtime: the administrative trains, polling interval, max active jobs, worker count and default retry policy.

### User Settings

Per-session preferences: polling interval for live data, visibility toggles for dashboard sections.

## Requirements

Trax.Dashboard is a Razor Class Library built on Blazor Server with [Radzen](https://www.radzen.com/) components. Your host application needs:

- ASP.NET Core (the Web SDK)
- Interactive Server render mode enabled (Blazor Server)

The dashboard handles its own static assets and routing, so you don't need to configure Radzen separately.

## License

MIT. See [LICENSE](https://github.com/TraxSharp/Trax.Dashboard/blob/main/LICENSE).

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
