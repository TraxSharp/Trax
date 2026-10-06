---
layout: default
title: Dashboard
description: "Trax.Dashboard, the Blazor Server UI at /trax: setup, required web assets, the trains, data, effects and settings pages, manifest groups and options."
nav_order: 8
section: Packages
---

# Dashboard

Trax.Dashboard is the operations control room: a web UI for inspecting registered trains, browsing execution history, managing scheduled manifests, and monitoring the network. It mounts as a Blazor Server app at `/trax`, similar to how Hangfire's dashboard works at `/hangfire`.

The dashboard needs the Trax Scheduler in the same host: `AddMediator()` and then `AddScheduler()` inside `AddTrax(...)`. Its pages queue, run and cancel trains and change settings through the Scheduler's `IOperationsService`, the same service the GraphQL `operations` fields call, so `UseTraxDashboard()` refuses to start without it.

## Quick Setup

### Installation

```bash
dotnet add package Trax.Dashboard
```

Or in your `.csproj`:

```xml
<PackageReference Include="Trax.Dashboard" Version="1.61.0" />
```

Pin an exact version, the release these docs are checked against or a newer one, rather than a
floating `1.*`, which restores whatever was published last.

### Required: `RequiresAspNetWebAssets`

Every host that calls `UseTraxDashboard()` must set this property in its csproj `<PropertyGroup>`:

```xml
<PropertyGroup>
  <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
</PropertyGroup>
```

Without it, the build omits `_framework/blazor.web.js`, the Blazor Server circuit never establishes, and the dashboard renders but **every button click is a no-op**. The page looks fine; nothing reacts.

**Why isn't this automatic?** The Trax.Dashboard package can't propagate the property transitively. NuGet auto-imports `build/$PackageId.props`, but the Razor SDK overwrites that file during pack with static-asset imports, wiping anything we put there. Putting it in a `.targets` file is too late - the SDK reads `RequiresAspNetWebAssets` during the props phase, before NuGet targets are imported. It has to live in the consumer's csproj.

**Verifying it took effect.** After building your host:

```bash
curl -I http://localhost:5000/trax/_framework/blazor.web.js
# HTTP/1.1 200 OK   ← good
# HTTP/1.1 404      ← property not set, dashboard will be dead
```

If you see a 404, your csproj is missing the property. Set it and rebuild.

### Configuration

Two lines in `Program.cs`, and a decision about who may use it:

```csharp
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler()
);
builder.Services.AddTraxDashboard(o => o.RequireRoles("Admin"));

var app = builder.Build();

app.UseTraxDashboard();

app.Run();
```

`UseTraxDashboard()` refuses to start until the options name a policy (`RequirePolicy`),
roles (`RequireRoles`), or call `AllowAnonymousDashboard()`, because the dashboard can queue,
run and cancel trains and change scheduler settings. See
[UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) for what each does.

The posture is not checked only when the page loads. An open dashboard keeps re-checking it
against the host's `AuthenticationStateProvider`, when the provider reports a change and every
minute, and checks every click or other message the browser sends against the latest verdict.
Once the user no longer satisfies it, the dashboard closes the connection and the page reloads through the gated
endpoint. The dashboard registers no `AuthenticationStateProvider` of its own, so it sees a
revoked role or a sign-out only when the host's provider does. ASP.NET Core's default provider
keeps the user the connection arrived with; register a revalidating provider (ASP.NET Identity's
template does), or name a policy whose handlers read live state, to have a revocation take effect
in an open dashboard. The in-dashboard check has no `HttpContext`, so a policy handler that reads
it gets `null`.

`AddTraxDashboard()` requires `AddTrax()` to be called first. If it is missing, `AddTraxDashboard()` throws `InvalidOperationException` with a clear message directing you to add `AddTrax()`. Call it once: a second call throws rather than replacing the first call's options, posture included. `UseTraxDashboard()` throws `InvalidOperationException` naming `AddScheduler()` when the Scheduler is not registered.

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
| **Lifetime** | DI lifetime (Transient, Scoped, or Singleton) |

This is the same information the `TrainRegistry` uses internally, but surfaced in a UI instead of buried in reflection.

### Data Pages

The dashboard exposes pages for browsing what the data provider persists:

| Page | Description |
|------|-------------|
| **Metadata** | Train execution history: start/end times, success/failure, inputs/outputs. Includes a "Current Junction" column for InProgress trains, and a cancel button on every Pending or InProgress row. |
| **Logs** | Application log entries captured during train execution, newest first. See [Log grids](#log-grids). |
| **Manifests** | Scheduled job definitions. An interval schedule reads as hours, minutes and seconds with nothing rounded, so 90 seconds is `Every 1m 30s`. A manifest's detail page has a **Replay decisions on retry** switch, which sets whether its retries [replay the failed run's decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions), and **Run Now, Ask Afresh** beside **Run Now**. Both go through `IOperationsService.TriggerManifestAsync`, the call the API's `triggerManifest` makes, and show its message: whether a new run was queued, the manifest's queued run brought forward, or one already due released as the trigger. When the dispatcher already claimed the queued retry, **Run Now, Ask Afresh** is too late to change it: the page shows a warning, not a success, naming the run whose decisions the retry still replays. On a host whose store is in memory nothing dispatches the queue, and the trigger is shown as refused. A manifest group's **Run Group** goes through `IOperationsService.TriggerManifestGroupsAsync`, as the API's `triggerGroup` does, and says how many members were queued and how many already had a queued run. |
| **Manifest Groups** | Manifest group settings and aggregate execution stats. A group's detail page has a "Cancel All Running" button. |
| **Dead Letters** | Failed jobs that exhausted their retry budget. **Requeue All** and **Requeue All, Ask Afresh** start the scheduler's background requeue-all job, [`IDeadLetterRequeueJobs`](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs), the one the API's `requeueAllDeadLetters` starts, and return at once: the page shows the running job above the grid, with a bar that fills as it requeues (the dead letters requeued so far out of those awaiting when it started), reads it every second, and when it ends reports its count and message, or shows why it stopped, and reloads the grid. The job runs on the server, so leaving the page does not stop it. While a requeue-all is already running on this server, pressing either button starts nothing: the page warns, with the job's message, and follows the running one. **Acknowledge All** and **Acknowledge Selected** ask for a resolution note of at most 1,000 characters, the limit the scheduler and the API's acknowledge mutations hold every note to. When the scheduler acknowledges nothing, because it refused the request or nothing was awaiting, the page shows its message as a warning and keeps the note and the selection so you can correct them. |
| **Work Queue** | Entries waiting for dispatch. The **Subject** column shows the entry's [subject key](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing) when the train sets one. The **Confirmed** column shows **Yes** for a confirmed entry, and a **Staged** badge on a queued entry that is not yet confirmed, which the dispatcher will not claim (an unconfirmed entry that is no longer queued, such as one the stale sweep cancelled, shows a dash). The subject key is computed by your train, so it may carry record identifiers. An entry's detail page shows **Waiting On** when the entry cannot be dispatched yet because of its subject: "Entry N, which is running for the same subject" when another entry's run holds the subject, or "Entry N, which is ahead of it for the same subject" when a queued sibling comes first, since dispatch offers only the first queued entry per subject each cycle. The sibling named is one dispatch would actually take first: it applies the same filters as dispatch, so a sibling whose `scheduledAt` has not arrived, or whose manifest group is disabled, is not named however high its priority. The detail page reads the entry, its masked input and what it is waiting on through [`IOperationsService.GetWorkQueueEntryDetailAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#read-models), the call behind the API's `workQueue.detail`. It shows the entry's input with its [`[TraxSensitive]`](/docs/sdk-reference/configuration/save-train-parameters#masking-sensitive-fields) members written as `{"_redacted": true}`, by the rule the API applies to the same read (see [Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate)): the stored input keeps the real values because the run starts from it, so the page reads it back as the input type of the train registered on this host and writes it masked. When no registered train takes that type, or the input does not read as it, the whole input is shown as `{"_redacted": true}`. |

These pages are accessible from the **Data** section in the sidebar navigation.

The list grids read only the columns they show, so paging through runs, work queue entries or manifests never loads an input, output, stack trace or properties document; the detail page loads those. The log grids are the exception, described below. The total under the pager is counted again when the filter changes, when the page shows it is too small, and otherwise at most every 30 seconds, so while the filter is unchanged it can lag the table by up to 30 seconds. A short last page gives the exact total.

#### Batch actions

The Metadata, Work Queue, Dead Letters, Manifests and Manifest Groups grids let you tick rows and act on them together. A selection holds the rows' ids, so rows ticked on one page stay selected after you move to another.

| Page | Action | Calls |
|------|--------|-------|
| Metadata | **Cancel Selected** | `IOperationsService.CancelExecutionsAsync` |
| Work Queue | **Cancel Selected** | `IOperationsService.CancelWorkQueueEntriesAsync` |
| Manifests | **Enable Selected**, **Disable Selected** | `IOperationsService.SetManifestsEnabledAsync` |
| Manifests | **Replay On Retry**, **Ask Afresh On Retry** | `IOperationsService.SetManifestsReplayDecisionsOnRetryAsync` |
| Manifest Groups | **Enable Selected**, **Disable Selected** | `IOperationsService.SetManifestGroupsEnabledAsync` |
| Manifest Groups | **Enable All**, **Disable All** | `IOperationsService.SetAllManifestGroupsEnabledAsync` |
| Manifest Groups | **Cancel Running** | `IOperationsService.CancelManifestGroupsAsync` |
| Manifests | **Trigger Selected**, **Trigger Selected, Ask Afresh** | `IOperationsService.TriggerManifestsAsync` |
| Manifest Groups | **Trigger Selected** | `IOperationsService.TriggerManifestGroupsAsync` |

The API's batch mutations make the same calls (see [IOperationsService: Batch actions](/docs/sdk-reference/scheduler-api/i-operations-service#batch-actions)), so the dashboard and the API change the same rows. The page shows the service's message, and its count includes only the rows that were cancelled or changed: a run already finished, or a manifest already in the requested state, is not counted. A count of zero is shown as a warning. The service takes at most 1,000 ids at once; a larger selection, or an empty one, is refused with the service's message, and the selection is kept so you can narrow it and try again.

**Trigger Selected** sends the whole selection in one call, as the batch actions above do (see
[Batch triggers](/docs/sdk-reference/scheduler-api/i-operations-service#batch-triggers)). The notification shows the
service's line, such as "2 queued, 1 already queued (that entry now runs as the trigger) across 3 of 3 manifest(s)." A
manifest that already had a queued entry gets nothing more queued: that entry runs as the trigger. On the groups page the
counts are of the groups' manifests. The service notes each id it could not trigger as asked, an id that names nothing or a
retry the dispatcher claimed first, and the page lists the notes under the notification, which is then a warning, as it is
when nothing was triggered. An accepted trigger clears the selection; a refused one keeps it.
**Trigger Selected, Ask Afresh** on the Manifests page sends `askAfresh`, so a queued retry it releases asks its deciders
afresh instead of replaying the failed run's decisions. A retry the dispatcher claimed first still replays; the service
counts it as "already dispatched and still replaying" and notes the run whose decisions it replays. Every ask-afresh button
shows as busy while its call runs.

#### Log grids

The **Logs** page and the logs grid on a run's detail page read through
[`IOperationsService.GetLogsAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#logquery), the read behind the
API's `operations.logs` query, and take the total under the pager from `CountLogsCappedAsync`. The Logs page reads newest
first; a run's page reads its own entries oldest first, top to bottom. Sorting the **Id** column reverses either; no other
column sorts.

Their filters sit above the grid, not in the column headers, because they are the filters the service serves: **Run
(Metadata Id)** on the Logs page, **Minimum level**, **Category contains** and **Message contains**. The text filters
ignore case. The exception text cannot be searched, and there is no filter on any other column. A changed filter goes
back to the first page. With a text filter the total is counted up to 10,000 entries; when more match, the page says so
above the grid and the pager stops at 10,000, so narrow the filter, or filter by run or level, to reach the rest. Each
entry the service returns carries its stack trace, capped at 4,000 characters, though the grid does not show it.

#### Dead Letter Detail Page

Clicking the visibility icon on a dead letter row opens a detail page with:

- **Dead Letter Details**: Status badge, dead-lettered timestamp, retry count, reason, resolution info
- **Manifest Details**: Linked manifest name, schedule, max retries, timeout, and properties JSON, masked by the same rule as a work queue entry's input
- **Most Recent Failure**: The latest failed execution's failure junction, exception, reason, stack trace, and input
- **Failed Execution History**: A grid of the failed metadata runs for the manifest, read from the database a page at a time, each linking to the metadata detail page

Two action buttons appear when the dead letter is in `AwaitingIntervention` status:

- **Re-queue**: Creates a new WorkQueue entry from the manifest, marks the dead letter as `Retried`, and navigates to the new work queue entry. The run [replays the failed run's decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions) when that is sound.
- **Re-queue, Ask Afresh**: The same, except the run asks its deciders afresh
- **Acknowledge**: Prompts for a resolution note of at most 1,000 characters, marks the dead letter as `Acknowledged`, and reloads the page. A note the scheduler refuses is shown and nothing changes

#### Metadata Detail Page

Clicking a metadata row opens a detail page with train state, timing, input/output, and exception details, and the run's log entries oldest first (see [Log grids](#log-grids)).

When a detail page (metadata, dead letter, work queue entry, manifest or manifest group) cannot load its row, because the database is unreachable for example, it says it could not load the row and why, rather than that the row does not exist, and keeps retrying on the polling interval until a load succeeds. A failed load never ends the operator's connection.

**State Transition Timeline.** A visual horizontal stepper at the top of the detail page shows the train's state progression: Pending -> InProgress -> Completed/Failed/Cancelled. Each state is color-coded and displays the timestamp when that state was reached, along with the duration between transitions (wait time, execution time). Past states are filled, the current state pulses, and future states are dimmed.

**Exception Viewer.** When a train has failed, the failure details card shows a **Failure Class** field with the run's [failure classification](/docs/core/trains-and-junctions#classifying-failures) (`Unclassified` unless a registered classifier assigned one), and a collapsible stack trace viewer with:
- Syntax highlighting for C# stack traces (method names, file paths, and line numbers each in distinct colors)
- A **Copy** button for copying the raw stack trace to the clipboard
- Auto-collapse for long stack traces (expanded by default for short ones)
- Inner exception separator formatting

**Replays Decisions Of.** When the run was queued to replay an earlier run's [decisions](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions), by a re-queue or by a manifest's retry, this field links to that run.

**Junction Timeline.** On a host that calls [`AddJunctionEvents()`](/docs/effect/junction-events), the page draws one row per step the run took, read from `trax.junction_run` through `JunctionRunQueries.ForRun`, the query the API's `operations.junctionRuns` uses:
- Each step's number, and for a step after a routing step, an indent and "on track of step #N"
- A bar placed against the run's start and coloured by state; a running step extends to now on each refresh, and a step with no recorded end on a finished run (a host that crashed, a dropped write) is drawn to the run's end and labelled "end not recorded"
- A step whose name the run withheld, after a `[TraxSensitive]` route, shown as "withheld"; its stored name never reaches the page. For a question or route step withheld this way, the question and the answer both read "withheld", with no confidence
- For a question, its key, the answer the run acted on, the confidence, and a **replayed** badge when the answer came from an earlier run; an answer to a [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) question reads "withheld"
- For a failed or cancelled step, its failure class and exception type, never its message
- The run's attempt, when it is a run of a manifest

The page reads at most the first 500 steps, the API's page size, and says so when the run recorded more. A run with no recorded steps shows a hint naming `AddJunctionEvents()`.

While the run is going, each refresh reads only the steps after the last one shown and those still in progress. Once the run has ended, the page reads every step on the load that first sees it ended and once more, to catch a step stored just after the run ended, and then stops; a change after that shows on a reload. A junction whose end was dropped stays in progress in the stored rows after its run has ended, and the timeline draws it to the run's end, labelled "end not recorded".

When `AddJunctionProgress()` is registered and the train is `InProgress`, a **Junction Progress** card appears showing:
- **Currently Running** - the name of the junction currently executing
- **Junction Started** - when the junction began (HH:mm:ss)

A **Cancel** button appears for Pending and InProgress trains. It calls `IOperationsService.CancelExecutionsAsync`, the call behind the GraphQL `cancelExecution` mutation, which sets `cancel_requested = true` in the database and also cancels the run at once through `ICancellationRegistry` when it is running on this host. A Pending run is recorded `TrainState.Cancelled` and never runs; an InProgress run stops at its next junction boundary when the host uses the junction progress provider. Cancelling a run that is missing or has already finished fails with "Execution {id} is not cancellable (missing or already terminal).", as the mutation does.

#### Run Train with Custom Inputs

The dashboard supports running any registered train with **custom inputs**, a capability that differentiates Trax from Hangfire, which can only requeue jobs with their original inputs.

- **From the Trains page**: Click the **Queue** button next to any train to open a dialog with a form builder (auto-generated from the input type's properties) or a raw JSON editor.
- **From the Trains page**: Click the **Run** button beside **Queue** to open the same form builder and JSON editor, and run the train now instead of queueing it.
- **From the Metadata Detail page**: Click the **Re-queue** button to re-run a train with its original input. The button calls `IOperationsService.RequeueExecutionAsync`, the method behind the API's `requeueExecution`. When the original recorded [decisions](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions), or was itself a re-queue that replays them, the new run replays them, so it takes the tracks the original took. **Re-queue, Ask Afresh** beside it makes the same call with `askAfresh`, so the new entry has no replay link and the run asks every question again. The notification shows the service's message, which says when a re-queue asks afresh because another run or queued entry already replays the original's decisions; a run's answers are replayed once. It is refused, with the message the API's `requeueExecution` gives, when the run has no saved input (inputs are saved only with `SaveTrainParameters()`), when what was saved is a placeholder rather than the input (`_truncated` for one over `MaxParameterBytes`, `_unserializable` or `_disposed`), or when the saved input has `[TraxSensitive]` members masked. Each of those would read back as default values, and the train would run with values it never had. It is also refused when the run's train is no longer registered, or when its saved input no longer reads as the train's input type; the message names the run, as the API's does.

The queue dialog and **Re-queue** go through `IOperationsService.QueueTrainAsync`, which enqueues through `ITrainExecutionService.QueueAsync`, the same path as the GraphQL `queueTrain` mutation, so the train's `OnQueue` hook fires, its subject key is stamped and the input size cap applies. They enqueue inside a trusted scope (`"dashboard"`), so per-train `[TraxAuthorize]` requirements do **not** apply: the dashboard is the admin surface, gated as a whole by its host, and anyone who can reach it can queue any train. The scope also covers the train's `OnQueue` hook and `QueueSubjectKey`, and anything they run or enqueue through `ITrainExecutionService` (including work started with `Task.Run`), so those skip their own `[TraxAuthorize]` requirements too. Protect the dashboard route accordingly. **Run** is different: it calls `IOperationsService.RunTrainAsync` inside the same trusted scope, the call behind the GraphQL [`runTrain`](/docs/sdk-reference/graphql-api/mutations#runtrain) mutation, then opens the run's detail page. The service reads the input, runs the train's `OnQueue` hook on it before the run's row is written (a hook that throws refuses the run), applies the input size cap, writes the run's row and submits it to the job submitter the train is routed to. No work queue entry is written, so the run skips dispatch priority, group `MaxActiveJobs` and [subject serialization](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing). Use **Queue** when a run must wait its turn. Dead-letter **Re-queue** and manifest triggers re-run what a manifest fixed and are likewise governed by access to the dashboard itself. See [Authorization: The Operations Surface](/docs/authorization#the-operations-surface).

Because Run writes no work queue entry, `QueueSubjectKey` does not apply to it. That makes it a documented bypass of [subject serialization](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing): a run started from it can execute concurrently with queued or in-flight work for the same subject. `RunTrainAsync` runs a train that overrides `QueueSubjectKey` only for a trusted caller such as the dashboard, and refuses any other caller, telling it to queue the train instead. For such a train the **Run** dialog shows a warning saying so and pointing at **Queue**, which waits for the subject to be free. A train that does not override `QueueSubjectKey` has nothing to bypass, and its dialog shows no warning. `Trax.Docs/adr/0019` records why Run bypasses serialization rather than waiting or refusing.

**How the input is read.** Both dialogs offer a **Form** tab, with one field per public property of the input type, and a **JSON** tab. JSON is passed to the service as typed, and the service reads it the way it reads JSON from any caller: property names in any case, and a property given twice, in the same or another casing, refused. The form reads each field the same way whatever culture or time zone the server runs in:

| Field | Read as |
|-------|---------|
| Number | Invariant culture: `.` for the decimal point, no thousands separators. `1.5` is one and a half on every server. |
| Date and time (`DateTime`, `DateTimeOffset`) | UTC when the text gives no offset, as every timestamp in the dashboard is. |
| Enum | A drop-down of the enum's names. |
| Other structured value | JSON for that type. |
| Blank, for a nullable property | `null`. |
| Blank, for a non-nullable `string` | An empty string. |
| Blank, for any other non-nullable property | Refused: "A value is required." |

A field whose text does not read as its type is refused by name, saying what was expected, and nothing is sent. The JSON tab takes up to 1,048,576 characters. The browser sends a long input to the server in small pieces, so pasting one does not trip the SignalR hub's 32 KB message limit or require raising it.

#### Home Page

The home page (`/trax`) reads its numbers through `IOperationsService.GetDashboardMetricsAsync`, the same data the GraphQL `operations.metrics.dashboard` query returns, and refreshes on each polling cycle. It leaves out administration trains when **Hide Administration Trains** is on, and reads the same on Postgres, Sqlite and InMemory hosts. Its panels, each of which can be hidden from the User Settings page:

- **Server Health**: CPU, working set, GC heap and uptime of the process hosting the dashboard (not the workers).
- **Summary Cards**: executions started today (UTC, any state), success rate, currently running, and dead letters awaiting intervention. The success rate is completed / (completed + failed), so cancelled runs do not count against it; cancellation is an operator action, not a failure.
- **Executions Chart**: completed, failed and cancelled executions per hour over the last 24 hours, or per minute over the last 60.
- **Avg Execution Duration (7d)**: the trains with the longest average completed run time.
- **Failures**: **Top Failing Trains (7d)**, and **Throughput (7d)**, completed runs per train for the top 3 trains plus "Other".

### Effects Page

The **Effects** page (`/trax/settings/effects`) shows all registered effect and junction effect provider factories. From this page you can:

- **Enable/disable** toggleable effects at runtime (changes apply to the next train execution scope). The GraphQL API does the same with [`operations.setEffectEnabled`](/docs/sdk-reference/graphql-api/mutations#seteffectenabled)
- **Configure** effects that expose runtime settings. Click the gear icon to open a dynamic form dialog

The page and the dialog read and write through [`IEffectSettingsService`](/docs/sdk-reference/scheduler-api/i-effect-settings-service), which `AddScheduler()` registers and the API's effects query and mutations call, so the two surfaces show the same values and refuse the same changes.

**Save** applies only the toggles you changed since the page loaded, each through `SetEffectEnabled`, then reads every effect's state again. A toggle you left alone is not applied, so an effect another operator or the `setEffectEnabled` mutation turned off in the meantime stays off. A toggle the service refuses is reported, and the others are still applied. **Discard Changes** drops your unsaved toggles and shows the effects' current state.

Configurable effects (those whose factory implements `IConfigurableEffectProviderFactory<TConfiguration>`) show a settings button in the grid. Clicking it opens a form built from the fields the service describes for the configuration type's public read-write properties. For example, the [Parameter Effect](/docs/effect/effect-providers/parameter-effect) exposes `SaveInputs` and `SaveOutputs` toggles. A boolean is a switch, an enum a drop-down, and a number, string, date, time, duration, `Guid` or `char` a text field read by the same rules as the Run and Queue forms. Any other property, such as a predicate delegate, is listed as set in code (or not set) and is never written, because its text form cannot be read back. A blank field is `null` for a property that accepts null and refused for one that does not, and a value must pass the property's own `ValidationAttribute`s.

A setting marked [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive) is never shown: the service does not return its value, so the dialog says only whether one is set and offers a masked field to set a new value. Left blank, the setting keeps its value.

**Save** sends `ConfigureEffect` only the fields you changed, so a value saved from elsewhere while the dialog was open is not reverted. The service writes them all or none: it converts and validates every field before applying any, and if a setter throws part way, puts back the fields already written. A refusal is shown in the dialog, which stays open. Closing the dialog without saving writes nothing. The settings object is per process and nothing is persisted, so a restart restores the configured values.

The Effects page was previously a section within Server Settings and has been moved to its own dedicated page under **Settings > Effects** in the sidebar.

### Server Settings

The **Server Settings** page (`/trax/settings/server`) edits the scheduler's runtime settings and the host's log levels. The scheduler settings are grouped as **Administrative Trains** (the ManifestManager and JobDispatcher switches), **Polling & Queue** (polling interval, `MaxActiveJobs`, and the local worker count when local workers are registered), **Retry Settings** (`DefaultMaxRetries`, **Failure Count Window**, retry delay, backoff multiplier and maximum delay), **Job Settings** (`DefaultJobTimeout`, `StalePendingTimeout`, startup recovery), **Dead Letter Settings**, and **Metadata Cleanup** when [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) is registered.

**Save** sends only the fields you changed since the page loaded, through `IOperationsService.UpdateSchedulerConfigAsync`, the call behind the GraphQL [`updateScheduler`](/docs/sdk-reference/graphql-api/mutations#config-nested-namespace) mutation, so a save stores only those settings and does not put back a value another operator or the mutation changed in the meantime. The form then reloads the settings in force. A saved setting reaches every scheduler host and replaces the value the host configured in code (see [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler#remarks)).

**Discard Changes** drops your unsaved edits and reloads the settings in force. There is no button that returns a setting to the value configured in code: once a setting is saved, the stored value is what every scheduler host runs with.

Each duration is held to the range the service accepts for that setting (see [Value Ranges](/docs/sdk-reference/scheduler-api/add-scheduler#value-ranges)). A duration outside it shows a message under the field and disables **Save** until it is corrected.

**Logging** lists each category configured under `Logging:LogLevel`, `Default` first, with the level its loggers filter at now. It reads and saves through [`ILogLevelService`](/docs/sdk-reference/scheduler-api/i-log-level-service), which `AddScheduler()` registers and the API's log levels query and mutation call. **Save** sends only the levels you changed. The service applies them to this process's logger filters, over every configuration source, until the process restarts; other processes of the deployment keep their own levels. When the host sets a category's filter itself after Trax, the level saved is not the one in force, and the page says which categories that is. The section is hidden when the host configures no `Logging:LogLevel` category.

### Manifest Groups

Every manifest belongs to a **ManifestGroup**, a first-class entity with per-group dispatch controls. The **Manifest Groups** page shows one row per group with its settings and aggregate stats: manifest count, total executions, completed, failed, in progress, and last run time. The counts come from `IOperationsService.GetManifestGroupExecutionStatsAsync`, the call the API's `stats` query for manifest groups makes, so the two always agree; a manifest's detail page reads its counts from `GetManifestExecutionStatsAsync` the same way.

Clicking a group opens a detail page with two sections:

- **Group Settings**: configurable `MaxActiveJobs` (per-group concurrency limit), `Priority` (0-31 dispatch ordering), and `IsEnabled` (disable all manifests in the group). Changes take effect on the next polling cycle. Unsaved edits are kept while the page polls, until you save or reset them. Save sends only the settings you changed, so it does not put back a value someone else changed since. Navigating to another group drops the edits, and every action on the page applies to the group in the address bar.
- **Group Data**: lists every manifest in the group along with their recent executions.

Per-group `MaxActiveJobs` prevents starvation: when a high-priority group hits its concurrency cap, lower-priority groups can still dispatch. This is configured from the dashboard, not from code.

### Persisted Operations (optional)

When the host registers the persisted-operations service, the dashboard exposes a **Persisted Operations** entry under **Data**. Either registration does it: [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) on the Trax GraphQL builder, or `AddPersistedOperationStore(...)` on a host that serves no GraphQL. The page lists the rows in `trax.persisted_operation` a page at a time, filtered by tenant (all, the default, or a named one), status and id prefix, and supports Upload, Edit, Deactivate, and Restore. A row is addressed by its tenant and its id, so two rows may share an id: each opens its own detail page (`/trax/data/persisted-operations/{id}?tenant={key}`, no `tenant` for the default) and changes only itself. The pages read and write through `IPersistedOperationsService`, the service the GraphQL `operations.persistedOperations` fields call, so they filter, page and refuse input exactly as the API does, with the API's messages. The editor renders parse, schema-validation, and shape-diff errors inline, and takes a document of up to 1,048,576 characters. A database error while loading, deactivating or restoring is logged and shown on the page. Each write re-checks the dashboard's authorization posture before it runs.

When the dashboard runs in a process of its own, register the store there and say how a change reaches the GraphQL nodes' caches: `AddPersistedOperationStore(store => store.UseRabbitMqInvalidation(rabbitConnectionString))` with the broker they use, or `store => store.SingleNode()` when no other process caches the operations. With neither, the host refuses to start. See [Persisted Operations](/docs/persisted-operations).

The sidebar entry is hidden when neither registration was made; direct navigation to the list or to an operation's detail page renders a "not enabled on this server" panel.

## How Discovery Works

Train discovery is handled by `ITrainDiscoveryService` in `Trax.Mediator`. When you call `AddMediator()`, it registers the discovery service, captures the `IServiceCollection`, and makes it available to both the dashboard and the [REST/GraphQL API](/docs/api).

At request time, the discovery service scans the registered `ServiceDescriptor` entries for anything that implements `IServiceTrain<,>`, extracts the generic type arguments, and deduplicates by input type (preferring interface registrations over concrete types).

If you register trains with `AddMediator` (which calls `AddScopedTraxRoute` under the hood), they show up automatically. Trains registered manually via `AddScoped<IMyTrain, MyTrain>()` will also appear as long as their interface extends `IServiceTrain<TIn, TOut>`.

## Options

```csharp
builder.Services.AddTraxDashboard(options =>
{
    options.Title = "My App";  // Header text (default: "Trax")
    options.RequirePolicy("TraxAdmin");
});
```

The dashboard is served at `/trax` and the path is not configurable: every page carries a
compile-time `@page "/trax/..."` route. The `routePrefix` argument to `UseTraxDashboard` only
builds the sidebar navigation links, so passing anything else leaves the pages in place and
breaks the links:

```csharp
app.UseTraxDashboard();  // served at /trax
```

## Layout

The dashboard uses [Radzen Blazor](https://blazor.radzen.com/) v11 components with a sidebar navigation layout. A theme toggle in the header switches between light and dark mode, with the preference persisted in `localStorage`.

When a page's refresh fails, the header shows **Last refresh failed** (the error as its tooltip) until a refresh succeeds; the rows on screen are then from the last refresh that worked. A grid that loads its rows page by page from the server clears them and shows the error when a load fails, rather than keeping the previous filter's or page's rows. A page's first load and the reload after navigating to another row are refreshes too: a failure is shown and retried on the next tick, and never ends the operator's connection. A custom `IDashboardSettingsService` receives failures through `NotifyPollFailed` and exposes the latest as `LastPollError`; both have default implementations.

### User Settings

The **User Settings** page (`/trax/settings/user`) lets each user customize their dashboard experience. Settings are stored in browser `localStorage` and only affect the current session.

| Setting | Default | Description |
|---------|---------|-------------|
| **Polling Interval** | 5 seconds | How often dashboard pages re-query for fresh data. Range: 1–300 seconds. |
| **Hide Administration Trains** | `true` | Exclude scheduler internals (ManifestManager, JobDispatcher, JobRunner, MetadataCleanup, DeadLetterCleanup) from statistics, charts, the Trains page and the Metadata and Manifests lists. A train is hidden only when its name is one of theirs exactly, as the API's `hideAdminTrains` filter matches, so a train of your own whose name merely ends the same way stays visible. |
| **Dashboard Components** | All visible | Toggle visibility of the home page panels: Server Health, Summary Cards, Executions Chart, Failures (which also holds the throughput chart) and Avg Execution Duration. |

## Integration with Existing Blazor Apps

If your application already uses Blazor Server, the dashboard's `AddTraxDashboard()` call is safe to use alongside your existing `AddRazorComponents()`. The dashboard pages use their own layout, so they won't interfere with your app's UI.

If your application is a minimal API or MVC app that doesn't use Blazor, the dashboard adds the necessary Blazor Server infrastructure automatically.

## Troubleshooting

### "Page doesn't load" or blank screen

`UseTraxDashboard()` needs to be called after `builder.Build()` and before `app.Run()`. If it's missing or misordered, the Blazor endpoints won't be mapped.

```csharp
var app = builder.Build();

app.UseTraxDashboard();  // After Build(), before Run()

app.Run();
```

### "UseTraxDashboard() requires the Trax Scheduler"

The host registers no Scheduler. The dashboard's pages work through the Scheduler's
`IOperationsService`, so add `AddScheduler()` after `AddMediator(...)` inside `AddTrax(...)`.
Without it the pages would fail on their first request, so `UseTraxDashboard()` refuses to map
them.

### "AddTraxDashboard() has already been called on this host"

Two calls to `AddTraxDashboard()` reached the same service collection, often one in a shared
bootstrap and one in the host. Keep one, with every dashboard option in it: a second call would
have replaced the first one's authorization posture.

### "UseTraxDashboard() needs to know who may use the dashboard"

No authorization posture was chosen. Add one to the `AddTraxDashboard` options:
`RequirePolicy("<name>")`, `RequireRoles("<role>")`, or `AllowAnonymousDashboard()` when a
fallback policy or an ingress rule in front of `/trax` is the gate.

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
    .AddScheduler()
);
builder.Services.AddTraxDashboard(o => o.RequireRoles("Admin"));  // After AddTrax() and trains are registered
```

If `AddTrax()` is missing entirely, `AddTraxDashboard()` throws `InvalidOperationException`.

### Blazor static assets returning 404

If styles are missing or `_content/` paths return 404, check that `UseStaticFiles()` is in your middleware pipeline. `UseTraxDashboard()` calls it internally, but if something earlier in the pipeline is short-circuiting requests, the static file middleware might not run.

### Duplicate train entries in the grid

`AddScopedTraxRoute<IMyTrain, MyTrain>()` registers two DI descriptors: one for the concrete type and one for the interface. The discovery service attempts to deduplicate these, but in some cases both registrations appear in the grid. The entries will have the same input/output types; one will show the interface name and the other the concrete class name.

This is cosmetic and doesn't affect train execution. If it bothers you, it's a known limitation of how the discovery service groups factory-based descriptors.

## Architecture

The dashboard sits near the bottom of the dependency tree, below the Scheduler and the API's persisted-operations package:

```
Trax.Effect ── Trax.Effect.Data
    └── Trax.Mediator
            └── Trax.Scheduler
                    └── Trax.Api.GraphQL.PersistedOperations
                            └── Trax.Dashboard (UI)
```

It references `Trax.Effect`, `Trax.Effect.Data`, `Trax.Mediator`, `Trax.Scheduler` and `Trax.Api.GraphQL.PersistedOperations`, and no database provider: the host picks one. At runtime it needs the Scheduler registered (`UseTraxDashboard()` refuses to start without it), and it shows the persisted-operations pages only when the host registers that service.

## SDK Reference

> [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/train-discovery)
