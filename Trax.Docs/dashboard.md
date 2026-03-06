---
layout: default
title: Dashboard
nav_order: 8
---

# Dashboard

Trax.Dashboard is the operations control room — a web UI for inspecting registered trains, browsing execution history, managing scheduled manifests, and monitoring the network. It mounts as a Blazor Server app at a route you choose, similar to how Hangfire's dashboard works at `/hangfire`.

The dashboard only requires `Trax.Effect`. As you add more Effect packages (Data, Scheduler, etc.), the dashboard gains access to more information. Start with train discovery, and add more as your setup grows.

## Quick Setup

### Installation

```bash
dotnet add package Trax.Dashboard
```

Or in your `.csproj`:

```xml
<PackageReference Include="Trax.Dashboard" Version="1.*" />
```

### Configuration

Two lines in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
);
builder.Services.AddTraxDashboard();

var app = builder.Build();

app.UseTraxDashboard("/trax");

app.Run();
```

`AddTraxDashboard()` requires `AddTrax()` to be called first. If it is missing, `AddTraxDashboard()` throws `InvalidOperationException` with a clear message directing you to add `AddTrax()`.

*SDK Reference: [AddTraxDashboard]({{ site.baseurl }}{% link sdk-reference/dashboard-api/add-trax-dashboard.md %}), [UseTraxDashboard]({{ site.baseurl }}{% link sdk-reference/dashboard-api/use-trax-dashboard.md %})*

Navigate to `/trax/trains` and you'll see every `IServiceTrain` registered in your application.

## What It Shows

### Trains Page

The dashboard scans your DI container for all services implementing `IServiceTrain<TIn, TOut>` and displays them in a sortable, filterable grid:

| Column | Description |
|--------|-------------|
| **Train** | The service interface name (e.g., `ICreateUserTrain`) |
| **Implementation** | The concrete class (e.g., `CreateUserTrain`) |
| **Input Type** | The `TIn` generic argument |
| **Output Type** | The `TOut` generic argument |
| **Lifetime** | DI lifetime—Transient, Scoped, or Singleton |

This is the same information the `TrainRegistry` uses internally, but surfaced in a UI instead of buried in reflection.

### Data Pages

When `Trax.Effect.Data` is registered, the dashboard exposes pages for browsing persisted data:

| Page | Description |
|------|-------------|
| **Metadata** | Train execution history—start/end times, success/failure, inputs/outputs. Includes a "Current Step" column for InProgress trains and per-row cancel buttons. |
| **Logs** | Application log entries captured during train execution |
| **Manifests** | Scheduled job definitions (requires Scheduler) |
| **Manifest Groups** | Manifest group settings and aggregate execution stats (requires Scheduler). Includes a "Cancel All Running" button. |
| **Dead Letters** | Failed jobs that exhausted their retry budget (requires Scheduler) |

These pages are accessible from the **Data** section in the sidebar navigation.

#### Dead Letter Detail Page

Clicking the visibility icon on a dead letter row opens a detail page with:

- **Dead Letter Details**: Status badge, dead-lettered timestamp, retry count, reason, resolution info
- **Manifest Details**: Linked manifest name, schedule, max retries, timeout, properties JSON
- **Most Recent Failure**: The latest failed execution's failure step, exception, reason, stack trace, and input
- **Failed Execution History**: A full grid of all failed metadata runs for the manifest, each linking to the metadata detail page

Two action buttons appear when the dead letter is in `AwaitingIntervention` status:

- **Re-queue**: Creates a new WorkQueue entry from the manifest, marks the dead letter as `Retried`, and navigates to the new work queue entry
- **Acknowledge**: Prompts for a resolution note, marks the dead letter as `Acknowledged`, and reloads the page

#### Metadata Detail Page

Clicking a metadata row opens a detail page with train state, timing, input/output, and exception details.

**State Transition Timeline** — A visual horizontal stepper at the top of the detail page shows the train's state progression: Pending → InProgress → Completed/Failed/Cancelled. Each step is color-coded and displays the timestamp when that state was reached, along with the duration between transitions (wait time, execution time). Past states are filled, the current state pulses, and future states are dimmed.

**Exception Viewer** — When a train has failed, the failure details card includes a collapsible stack trace viewer with:
- Syntax highlighting for C# stack traces (method names, file paths, and line numbers each in distinct colors)
- A **Copy** button for copying the raw stack trace to the clipboard
- Auto-collapse for long stack traces (expanded by default for short ones)
- Inner exception separator formatting

When `AddStepProgress()` is registered and the train is `InProgress`, a **Step Progress** card appears showing:
- **Currently Running** — the name of the step currently executing
- **Step Started** — when the step began (HH:mm:ss)

A **Cancel** button appears for InProgress trains. Clicking it sets `cancel_requested = true` in the database and attempts to cancel the train via `ICancellationRegistry` (instant for same-server). Cancelled trains transition to `TrainState.Cancelled`.

#### Run Train with Custom Inputs

The dashboard supports running any registered train with **custom inputs** — a capability that differentiates Trax from Hangfire, which can only requeue jobs with their original inputs.

- **From the Trains page**: Click the **Queue** button next to any train to open a dialog with a form builder (auto-generated from the input type's properties) or a raw JSON editor.
- **From the Metadata Detail page**: Click the **Re-queue** button to re-run a train with its original input.

#### Real-Time Metrics on Home Page

The dashboard home page includes real-time operational metrics that update on each polling cycle:

- **Queue Depth** — number of work items waiting to be dispatched
- **Completed/min** — jobs completed per minute (5-minute rolling window)
- **Failed/min** — jobs failed per minute (5-minute rolling window)
- **Throughput Chart** — per-minute completed/failed chart for the last 60 minutes

These complement the existing summary cards (executions today, success rate, currently running, dead letters, active manifests, registered trains).

#### Cancellation Metrics on Home Page

The dashboard home page includes:
- A **Cancelled** slice in the train state donut chart
- A **Cancelled** column series in the 24-hour execution chart

Cancelled trains are excluded from the success rate calculation — cancellation is an operator action, not a failure.

### Effects Page

The **Effects** page (`/trax/settings/effects`) shows all registered effect and step effect provider factories. From this page you can:

- **Enable/disable** toggleable effects at runtime (changes apply to the next train execution scope)
- **Configure** effects that expose runtime settings — click the gear icon to open a dynamic form dialog

Configurable effects (those whose factory implements `IConfigurableEffectProviderFactory<TConfiguration>`) show a settings button in the grid. Clicking it opens a form auto-generated from the configuration type's properties. For example, the [Parameter Effect](usage-guide/effect-providers/parameter-effect.md) exposes `SaveInputs` and `SaveOutputs` toggles.

The Effects page was previously a section within Server Settings and has been moved to its own dedicated page under **Settings > Effects** in the sidebar.

### Manifest Groups

Every manifest belongs to a **ManifestGroup** — a first-class entity with per-group dispatch controls. The **Manifest Groups** page shows one row per group with its settings and aggregate stats: manifest count, total executions, completed, failed, and last run time.

Clicking a group opens a detail page with two sections:

- **Group Settings**: configurable `MaxActiveJobs` (per-group concurrency limit), `Priority` (0-31 dispatch ordering), and `IsEnabled` (disable all manifests in the group). Changes take effect on the next polling cycle.
- **Group Data**: lists every manifest in the group along with their recent executions.

Per-group `MaxActiveJobs` prevents starvation — when a high-priority group hits its concurrency cap, lower-priority groups can still dispatch. This is configured from the dashboard, not from code.

## How Discovery Works

Train discovery is handled by `ITrainDiscoveryService` in `Trax.Mediator`. When you call `AddMediator()`, it registers the discovery service, captures the `IServiceCollection`, and makes it available to both the dashboard and the [REST/GraphQL API]({{ site.baseurl }}{% link api.md %}).

At request time, the discovery service scans the registered `ServiceDescriptor` entries for anything that implements `IServiceTrain<,>`, extracts the generic type arguments, and deduplicates by input type (preferring interface registrations over concrete types).

If you register trains with `AddMediator` (which calls `AddScopedTraxRoute` under the hood), they show up automatically. Trains registered manually via `AddScoped<IMyTrain, MyTrain>()` will also appear as long as their interface extends `IServiceTrain<TIn, TOut>`.

*SDK Reference: [TrainDiscovery]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-discovery.md %})*

## Options

```csharp
builder.Services.AddTraxDashboard(options =>
{
    options.Title = "My App";  // Header text (default: "Trax")
});
```

*SDK Reference: [AddTraxDashboard]({{ site.baseurl }}{% link sdk-reference/dashboard-api/add-trax-dashboard.md %}), [DashboardOptions]({{ site.baseurl }}{% link sdk-reference/dashboard-api/dashboard-options.md %})*

The route prefix is set in `UseTraxDashboard`:

```csharp
app.UseTraxDashboard("/admin/trax");
```

## Layout

The dashboard uses [Radzen Blazor](https://blazor.radzen.com/) v6 components with a sidebar navigation layout. A theme toggle in the header switches between light and dark mode, with the preference persisted in `localStorage`.

### User Settings

The **User Settings** page (`/trax/settings/user`) lets each user customize their dashboard experience. Settings are stored in browser `localStorage` and only affect the current session.

| Setting | Default | Description |
|---------|---------|-------------|
| **Polling Interval** | 5 seconds | How often dashboard pages re-query for fresh data. Range: 1–300 seconds. |
| **Hide Administration Trains** | `true` | Exclude scheduler internals (ManifestManager, JobRunner, MetadataCleanup) from statistics and charts. |
| **Dashboard Components** | All visible | Toggle visibility of individual home page sections (summary cards, charts, tables, real-time metrics, throughput chart). |

## Integration with Existing Blazor Apps

If your application already uses Blazor Server, the dashboard's `AddTraxDashboard()` call is safe to use alongside your existing `AddRazorComponents()`. The dashboard pages use their own layout, so they won't interfere with your app's UI.

If your application is a minimal API or MVC app that doesn't use Blazor, the dashboard adds the necessary Blazor Server infrastructure automatically.

## Troubleshooting

### "Page doesn't load" or blank screen

`UseTraxDashboard()` needs to be called after `builder.Build()` and before `app.Run()`. If it's missing or misordered, the Blazor endpoints won't be mapped.

```csharp
var app = builder.Build();

app.UseTraxDashboard("/trax");  // After Build(), before Run()

app.Run();
```

### "No trains listed"

The dashboard discovers trains by scanning `ServiceDescriptor` entries in your DI container. If the grid is empty:

**Causes:**
- The assembly containing your trains wasn't passed to `AddMediator`
- `AddTraxDashboard()` was called before the trains were registered, so the captured `IServiceCollection` snapshot doesn't include them yet

**Fix:** Make sure `AddTrax()` is called before `AddTraxDashboard()`, and that `AddTraxDashboard()` is called after the trains are registered:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
);
builder.Services.AddTraxDashboard();  // After AddTrax() and trains are registered
```

If `AddTrax()` is missing entirely, `AddTraxDashboard()` throws `InvalidOperationException`.

### Blazor static assets returning 404

If styles are missing or `_content/` paths return 404, ensure `UseStaticFiles()` is in your middleware pipeline. `UseTraxDashboard()` calls it internally, but if something earlier in the pipeline is short-circuiting requests, the static file middleware might not run.

### Duplicate train entries in the grid

`AddScopedTraxRoute<IMyTrain, MyTrain>()` registers two DI descriptors—one for the concrete type and one for the interface. The discovery service attempts to deduplicate these, but in some cases both registrations appear in the grid. The entries will have the same input/output types; one will show the interface name and the other the concrete class name.

This is cosmetic and doesn't affect train execution. If it bothers you, it's a known limitation of how the discovery service groups factory-based descriptors.

## Architecture

The dashboard sits alongside other Effect packages in the dependency tree:

```
Trax.Effect
    ├── Trax.Dashboard (UI)
    ├── Trax.Mediator (TrainBus)
    ├── Trax.Effect.Data (Persistence)
    └── ...
```

It depends only on `Trax.Effect`—no transitive dependency on Data, Mediator, Scheduler, or any database provider. The dashboard discovers what's available in your DI container and adapts accordingly.
