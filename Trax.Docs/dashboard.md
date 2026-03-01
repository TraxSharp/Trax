---
layout: default
title: Dashboard
nav_order: 8
---

# Dashboard

Trax.Dashboard adds a web UI to your application for inspecting registered workflows. It mounts as a Blazor Server app at a route you choose—similar to how Hangfire's dashboard works at `/hangfire`.

The dashboard only requires `Trax.Effect`. As you add more Effect packages (Data, Scheduler, etc.), the dashboard gains access to more information. Start with workflow discovery, and add more as your setup grows.

## Quick Setup

### Installation

```bash
dotnet add package Trax.Dashboard
```

Or in your `.csproj`:

```xml
<PackageReference Include="Trax.Dashboard" Version="5.*" />
```

### Configuration

Two lines in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax.CoreEffects(o => o.AddServiceTrainBus(typeof(Program).Assembly));
builder.Services.AddTrax.CoreDashboard();

var app = builder.Build();

app.UseTrax.CoreDashboard("/trax");

app.Run();
```

*API Reference: [AddTrax.CoreDashboard]({{ site.baseurl }}{% link api-reference/dashboard-api/add-trax-dashboard.md %}), [UseTrax.CoreDashboard]({{ site.baseurl }}{% link api-reference/dashboard-api/use-trax-dashboard.md %})*

Navigate to `/trax/workflows` and you'll see every `IServiceTrain` registered in your application.

## What It Shows

### Workflows Page

The dashboard scans your DI container for all services implementing `IServiceTrain<TIn, TOut>` and displays them in a sortable, filterable grid:

| Column | Description |
|--------|-------------|
| **Workflow** | The service interface name (e.g., `ICreateUserWorkflow`) |
| **Implementation** | The concrete class (e.g., `CreateUserWorkflow`) |
| **Input Type** | The `TIn` generic argument |
| **Output Type** | The `TOut` generic argument |
| **Lifetime** | DI lifetime—Transient, Scoped, or Singleton |

This is the same information the `WorkflowRegistry` uses internally, but surfaced in a UI instead of buried in reflection.

### Data Pages

When `Trax.Effect.Data` is registered, the dashboard exposes pages for browsing persisted data:

| Page | Description |
|------|-------------|
| **Metadata** | Workflow execution history—start/end times, success/failure, inputs/outputs. Includes a "Current Step" column for InProgress workflows and per-row cancel buttons. |
| **Logs** | Application log entries captured during workflow execution |
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

Clicking a metadata row opens a detail page with workflow state, timing, input/output, and exception details.

When `AddStepProgress()` is registered and the workflow is `InProgress`, a **Step Progress** card appears showing:
- **Currently Running** — the name of the step currently executing
- **Step Started** — when the step began (HH:mm:ss)

A **Cancel** button appears for InProgress workflows. Clicking it sets `cancel_requested = true` in the database and attempts to cancel the workflow via `ICancellationRegistry` (instant for same-server). Cancelled workflows transition to `WorkflowState.Cancelled`.

#### Cancellation Metrics on Home Page

The dashboard home page includes:
- A **Cancelled** slice in the workflow state donut chart
- A **Cancelled** column series in the 24-hour execution chart

Cancelled workflows are excluded from the success rate calculation — cancellation is an operator action, not a failure.

### Effects Page

The **Effects** page (`/trax/settings/effects`) shows all registered effect and step effect provider factories. From this page you can:

- **Enable/disable** toggleable effects at runtime (changes apply to the next workflow execution scope)
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

The dashboard reads from the same `IServiceCollection` that your application builds. When you call `AddTrax.CoreDashboard()`, it captures a reference to the service collection. At request time, it scans the registered `ServiceDescriptor` entries for anything that implements `IServiceTrain<,>`, extracts the generic type arguments, and deduplicates.

If you register workflows with `AddServiceTrainBus` (which calls `AddScopedTrax.CoreRoute` under the hood), they show up automatically. Workflows registered manually via `AddScoped<IMyWorkflow, MyWorkflow>()` will also appear as long as their interface extends `IServiceTrain<TIn, TOut>`.

## Options

```csharp
builder.Services.AddTrax.CoreDashboard(options =>
{
    options.Title = "My App";  // Header text (default: "Trax.Core")
});
```

*API Reference: [AddTrax.CoreDashboard]({{ site.baseurl }}{% link api-reference/dashboard-api/add-trax-dashboard.md %}), [DashboardOptions]({{ site.baseurl }}{% link api-reference/dashboard-api/dashboard-options.md %})*

The route prefix is set in `UseTrax.CoreDashboard`:

```csharp
app.UseTrax.CoreDashboard("/admin/trax");
```

## Layout

The dashboard uses [Radzen Blazor](https://blazor.radzen.com/) v6 components with a sidebar navigation layout. A theme toggle in the header switches between light and dark mode, with the preference persisted in `localStorage`.

### User Settings

The **User Settings** page (`/trax/settings/user`) lets each user customize their dashboard experience. Settings are stored in browser `localStorage` and only affect the current session.

| Setting | Default | Description |
|---------|---------|-------------|
| **Polling Interval** | 5 seconds | How often dashboard pages re-query for fresh data. Range: 1–300 seconds. |
| **Hide Administration Workflows** | `true` | Exclude scheduler internals (ManifestManager, TaskServerExecutor, MetadataCleanup) from statistics and charts. |
| **Dashboard Components** | All visible | Toggle visibility of individual home page sections (summary cards, charts, tables). |

## Integration with Existing Blazor Apps

If your application already uses Blazor Server, the dashboard's `AddTrax.CoreDashboard()` call is safe to use alongside your existing `AddRazorComponents()`. The dashboard pages use their own layout, so they won't interfere with your app's UI.

If your application is a minimal API or MVC app that doesn't use Blazor, the dashboard adds the necessary Blazor Server infrastructure automatically.

## Troubleshooting

### "Page doesn't load" or blank screen

`UseTrax.CoreDashboard()` needs to be called after `builder.Build()` and before `app.Run()`. If it's missing or misordered, the Blazor endpoints won't be mapped.

```csharp
var app = builder.Build();

app.UseTrax.CoreDashboard("/trax");  // After Build(), before Run()

app.Run();
```

### "No workflows listed"

The dashboard discovers workflows by scanning `ServiceDescriptor` entries in your DI container. If the grid is empty:

**Causes:**
- The assembly containing your workflows wasn't passed to `AddServiceTrainBus`
- `AddTrax.CoreDashboard()` was called before the workflows were registered, so the captured `IServiceCollection` snapshot doesn't include them yet

**Fix:** Make sure `AddTrax.CoreDashboard()` is called after `AddTrax.CoreEffects`:

```csharp
builder.Services.AddTrax.CoreEffects(o =>
    o.AddServiceTrainBus(typeof(Program).Assembly)
);
builder.Services.AddTrax.CoreDashboard();  // After workflows are registered
```

### Blazor static assets returning 404

If styles are missing or `_content/` paths return 404, ensure `UseStaticFiles()` is in your middleware pipeline. `UseTrax.CoreDashboard()` calls it internally, but if something earlier in the pipeline is short-circuiting requests, the static file middleware might not run.

### Duplicate workflow entries in the grid

`AddScopedTrax.CoreRoute<IMyWorkflow, MyWorkflow>()` registers two DI descriptors—one for the concrete type and one for the interface. The discovery service attempts to deduplicate these, but in some cases both registrations appear in the grid. The entries will have the same input/output types; one will show the interface name and the other the concrete class name.

This is cosmetic and doesn't affect workflow execution. If it bothers you, it's a known limitation of how the discovery service groups factory-based descriptors.

## Architecture

The dashboard sits alongside other Effect packages in the dependency tree:

```
Trax.Effect
    ├── Trax.Dashboard (UI)
    ├── Trax.Mediator (WorkflowBus)
    ├── Trax.Effect.Data (Persistence)
    └── ...
```

It depends only on `Trax.Effect`—no transitive dependency on Data, Mediator, Scheduler, or any database provider. The dashboard discovers what's available in your DI container and adapts accordingly.
