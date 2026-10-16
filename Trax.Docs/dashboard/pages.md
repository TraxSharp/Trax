---
title: Dashboard pages
description: "Reference for every Trax dashboard page: what it shows, the actions it offers, and the IOperationsService call and GraphQL field behind each."
type: reference
area: dashboard
nav_order: 2
source:
  - Trax.Dashboard/src/Trax.Dashboard/Components/Pages
---

# Dashboard pages

The pages the dashboard serves under `/trax`, in sidebar order. Every read and action goes
through the same service as the matching GraphQL `operations` field, mostly
[IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service), so the dashboard and
the API show the same rows and refuse the same requests with the same messages. Setup is in
[Add the dashboard to a host](/docs/dashboard).

## Home

`/trax`. Reads `IOperationsService.GetDashboardMetricsAsync`, the data behind
`operations.metrics.dashboard`, on each polling cycle. Administration trains are left out while
**Hide Administration Trains** is on. Each panel can be hidden from [User settings](#user-settings).

| Panel | Shows |
|---|---|
| **Server Health** | CPU, working set, GC heap and uptime of the process hosting the dashboard, not the workers. |
| **Summary Cards** | Executions started today (UTC, any state), success rate, currently running, dead letters awaiting intervention. Success rate is completed / (completed + failed); cancelled runs do not count. |
| **Executions Chart** | Completed, failed and cancelled executions per hour over the last 24 hours, or per minute over the last 60. |
| **Avg Execution Duration (7d)** | The trains with the longest average completed run time. |
| **Failures** | **Top Failing Trains (7d)**, and **Throughput (7d)**: completed runs per train for the top 3 trains plus "Other". |

## Trains

`/trax/trains`. Every `IServiceTrain<TIn, TOut>` the host registers, from
[ITrainDiscoveryService](/docs/sdk-reference/mediator-api/i-train-discovery-service).

| Column | Value |
|---|---|
| **Train** | The service interface, such as `ICreateUserTrain` |
| **Implementation** | The class, such as `CreateUserTrain` |
| **Input Type** | `TIn` |
| **Output Type** | `TOut` |
| **Lifetime** | Transient, Scoped or Singleton |

Each row has **Queue** and **Run**, which open the [run and queue dialogs](#run-and-queue-dialogs).

### Run and queue dialogs

Both dialogs take the input through a **Form** tab, one field per public property of the input
type, or a **JSON** tab.

| Button | Calls | GraphQL | Writes a work queue entry |
|---|---|---|---|
| **Queue** | `IOperationsService.QueueTrainAsync`, through `ITrainExecutionService.QueueAsync` | `queueTrain` | yes |
| **Run** | `IOperationsService.RunTrainAsync`, then opens the run's page | [`runTrain`](/docs/sdk-reference/graphql-api/mutations/work-queue#runtrain) | no |

Both run the train's `OnQueue` hook on the input and apply the input size cap. Queue also stamps
the subject key. Run runs `OnQueue` before the run's row is written (a hook that throws refuses
the run), writes the row and submits the run to the job submitter the train is routed to; with no
work queue entry it skips dispatch priority, the group's `MaxActiveJobs` and
[subject serialization](/docs/mediator/queued-work#two-mutations-for-one-record).
`RunTrainAsync` runs a train that overrides `QueueSubjectKey` only for a trusted caller such as the
dashboard, and refuses any other caller; for such a train the Run dialog shows a warning pointing
at **Queue**. `Trax.Docs/adr/0019` records why Run bypasses serialization.

Both enqueue inside the dashboard's trusted scope (`"dashboard"`), so the train's
`[TraxAuthorize]` requirements do not apply; the same holds for **Re-queue**, **Resume**,
dead-letter requeues and manifest triggers. What the scope covers and why is in
[Where Trax checks authorization](/docs/security/where-authorization-is-checked).

JSON is passed to the service as typed: property names in any case, and a property given twice,
in the same or another casing, refused. The JSON tab takes up to 1,048,576 characters, sent to the
server in small pieces, so a long input does not trip the SignalR hub's 32 KB message limit. Form
fields are read the same way whatever the server's culture or time zone:

| Field | Read as |
|---|---|
| Number | Invariant culture: `.` for the decimal point, no thousands separators. |
| Date and time (`DateTime`, `DateTimeOffset`) | UTC when the text gives no offset. |
| Enum | A drop-down of the enum's names. |
| Other structured value | JSON for that type. |
| Blank, nullable property | `null`. |
| Blank, non-nullable `string` | An empty string. |
| Blank, any other non-nullable property | Refused: "A value is required." |

A field whose text does not read as its type is refused by name, saying what was expected, and
nothing is sent.

## Data pages

Under **Data** in the sidebar. The list grids read only the columns they show, so paging never
loads an input, output, stack trace or properties document; the detail pages load those. The total
under the pager is counted again when the filter changes, when the page shows it is too small, and
otherwise at most every 30 seconds, so it can lag the table by up to 30 seconds while the filter is
unchanged. A short last page gives the exact total.

When a detail page (metadata, dead letter, work queue entry, manifest or manifest group) cannot
load its row, because the database is unreachable for example, it says it could not load the row
and why, and retries on the polling interval until a load succeeds.

### Metadata

Run history: start and end times, state, inputs and outputs. A **Current Junction** column for
InProgress runs, and a cancel button on every Pending or InProgress row.

### Metadata detail page

A run's train state, timing, input, output, exception, and its log entries oldest first (see
[Log grids](#log-grids)), with these parts:

| Part | Shows |
|---|---|
| **State Transition Timeline** | Pending -> InProgress -> Completed, Failed or Cancelled, each with the time it was reached and the durations between them. |
| **Failure Class** | The run's [failure classification](/docs/effect/failure-classification), `Unclassified` unless a registered classifier assigned one. |
| **Stack trace** | A collapsible viewer with C# highlighting, a **Copy** button, inner exception separators; long traces start collapsed. |
| **Replays Decisions Of** | The run whose [decisions](/docs/effect/decisions) this run was queued to replay, by a re-queue or a manifest's retry. |
| **Resumes** | The run this one resumed from a [checkpoint](/docs/sdk-reference/train-methods/checkpoint), and the step it resumed at or "after that run's latest checkpoint". |
| **Run Graph** | See [Run graph](#run-graph). |
| **Junction Timeline** | See [Junction timeline](#junction-timeline). |
| **Junction Progress** | With `AddJunctionProgress()` and an InProgress run: the junction running now and when it started (HH:mm:ss). |

| Action | Calls | GraphQL |
|---|---|---|
| **Cancel** (Pending or InProgress) | `IOperationsService.CancelExecutionsAsync` | `cancelExecution` |
| **Re-queue**, **Re-queue, Ask Afresh** | `IOperationsService.RequeueExecutionAsync` (with `askAfresh`) | `requeueExecution` |
| **Resume** | `IOperationsService.ResumeExecutionAsync` with no step | [`resumeExecution`](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution) without `from` |
| **Resume from here** (on the run graph) | `IOperationsService.ResumeExecutionAsync` with the step's node id | `resumeExecution` with `from` |

#### Cancel

Sets `cancel_requested` on the run and, when it runs on this host, cancels it at once through
`ICancellationRegistry`. A Pending run is recorded Cancelled and never runs; an InProgress run
stops at its next junction boundary when the host uses the junction progress provider. A run that
is missing or already finished is refused with "Execution {id} is not cancellable (missing or
already terminal).".

#### Re-queue

Re-runs the train with its original input. When the original recorded decisions, or was itself a
re-queue that replays them, the new run replays them; **Ask Afresh** queues it with no replay link,
so the run asks every question again. A run's answers are replayed once, and the message says when
a re-queue asks afresh because another run already replays them. Refused, with the API's message,
when the run has no saved input (inputs are saved only with `SaveTrainParameters()`), when the
saved input is a placeholder (`_truncated` for one over `MaxParameterBytes`, `_unserializable`,
`_disposed`), when it has `[TraxSensitive]` members masked, when the train is no longer registered,
or when the saved input no longer reads as the train's input type.

#### Resume

Shown only when the run graph says the run can resume after its latest checkpoint. Refused, with
the API's messages, for a run that is not failed or cancelled, one a state machine's step started,
a saved input a re-queue would refuse, a resume already queued, or no checkpoint that lets it
resume. On success the page opens the new work queue entry. A resume records no actor.

#### Run graph

The train's [declared chain](/docs/sdk-reference/train-methods/chain-graph) with this run on it,
matched by `RunGraphs.Match` as [`operations.runGraph`](/docs/sdk-reference/graphql-api/queries/executions#rungraph)
does:

- Each declared step in order, with its kind and name, and each routing step's tracks under it;
  the track the run took is marked "taken" with a solid rule, a fallback track "fallback".
- Each `Parallel` step's branches as side-by-side lanes, solid once the step started and dashed
  before. The step's own state comes from its branches: failed when any failed, cancelled when one
  was stopped, running until all have finished. On a narrow screen the lanes stack.
- Each step's state in words: completed, failed (with failure class and exception type),
  cancelled, running, skipped, not reached, not recorded (an `Extract`, `Seed` or `Resolve`, or a
  checkpoint the run did not store), withheld (after a route whose answer is withheld), restored
  (a step before the point a resumed run started at), or interrupted (started and never recorded
  its end in a run that has ended).
- An **opaque** badge on a step decided only at run time, **replayed** where an answer came from an
  earlier run, and **checkpoint** on a step holding a checkpoint the run can resume from (its
  existence and place, never its content).
- On a failed or cancelled run, **Resume from here** on each step the
  [resume check](/docs/sdk-reference/train-methods/checkpoint#what-can-resume-where) allows; a
  refusal shows the service's reason.
- **Steps not on the graph**: recorded steps that match no node.

A train with no graph on this host shows a message instead. The graph is the chain as the host
declares it now.

#### Junction timeline

On a host that calls [`AddJunctionEvents()`](/docs/effect/junction-events): one row per step the
run took, read from `trax.junction_run` through `JunctionRunQueries.ForRun`, the query behind
`operations.junctionRuns`.

- The step's number, and for a step after a routing step an indent and "on track of step #N".
- A bar placed against the run's start and coloured by state. A running step extends to now; a
  step with no recorded end on a finished run is drawn to the run's end and labelled "end not
  recorded".
- A step whose name the run withheld, after a `[TraxSensitive]` route, reads "withheld"; for a
  question or route step withheld this way, question and answer both read "withheld", with no
  confidence.
- For a question: its key, the answer the run acted on, the confidence, and **replayed** when the
  answer came from an earlier run. An answer to a
  [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) question
  reads "withheld".
- For a failed or cancelled step: its failure class and exception type, never its message.
- The run's attempt, for a run of a manifest.

At most the first 500 steps, the API's page size, with a note when there are more. A run with no
recorded steps shows a hint naming `AddJunctionEvents()`. While the run is going, each refresh
reads only new and in-progress steps; after it ends, the page reads every step twice and stops.

### Log grids

The **Logs** page and the logs grid on a run's page read through
[`IOperationsService.GetLogsAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#logquery),
the read behind `operations.logs`, and take the total from `CountLogsCappedAsync`.

| | Logs page | A run's page |
|---|---|---|
| Order | newest first | oldest first |
| Filters | **Run (Metadata Id)**, **Minimum level**, **Category contains**, **Message contains** | **Minimum level**, **Category contains**, **Message contains** |

Sorting the **Id** column reverses the order; no other column sorts. The text filters ignore case;
the exception text cannot be searched. A changed filter goes back to the first page. With a text
filter the total is counted up to 10,000 entries, and when more match the page says so and the
pager stops at 10,000. Each entry carries its stack trace, capped at 4,000 characters, which the
grid does not show.

### Manifests

Scheduled job definitions. An interval reads as hours, minutes and seconds with nothing rounded
(`Every 1m 30s`). Counts on a manifest's page come from `GetManifestExecutionStatsAsync`.

| On a manifest's page | Does | Calls |
|---|---|---|
| **Replay decisions on retry** switch | Sets whether its retries replay the failed run's [decisions](/docs/effect/decisions) | `SetManifestsReplayDecisionsOnRetryAsync` |
| **Run Now**, **Run Now, Ask Afresh** | Triggers the manifest | `IOperationsService.TriggerManifestAsync`, as `triggerManifest` |

A trigger shows the service's message: a new run queued, the manifest's queued run brought
forward, or one already due released as the trigger. When the dispatcher has already claimed the
queued retry, **Ask Afresh** is too late to change it, and the page shows a warning naming the run
whose decisions the retry still replays. On a host whose store is in memory nothing dispatches the
queue, and the trigger is shown as refused.

### Manifest groups

One row per group with its settings and counts: manifests, executions, completed, failed, in
progress, last run. Counts come from `GetManifestGroupExecutionStatsAsync`, as the API's `stats`
query.

A group's page has **Group Settings** (`MaxActiveJobs`, `Priority` 0 to 31, `IsEnabled`) and
**Group Data** (its manifests and their recent runs). Changes apply on the next polling cycle.
Unsaved edits survive polling; **Save** sends only the settings you changed; moving to another
group drops the edits, and every action applies to the group in the address bar. **Run Group**
calls `TriggerManifestGroupsAsync`, as `triggerGroup`, and says how many members were queued and
how many already had a queued run. **Cancel All Running** cancels the group's running runs.

### Dead letters

Runs that exhausted their retry budget. How to resolve one is in
[Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup).

| Action | Does |
|---|---|
| **Requeue All**, **Requeue All, Ask Afresh** | Starts the background requeue-all job, [`IDeadLetterRequeueJobs`](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs), as `requeueAllDeadLetters`, and returns at once. |
| **Acknowledge All**, **Acknowledge Selected** | Ask for a resolution note of at most 1,000 characters and acknowledge. |

While a requeue-all runs, the page shows it above the grid with a bar (requeued so far out of
those awaiting when it started), reads it every second, and when it ends reports its count and
message, or why it stopped, and reloads the grid. It runs on the server, so leaving the page does
not stop it. While one is already running on this server, either button starts nothing and the
page follows the running one. When the scheduler acknowledges nothing, refused or with nothing
awaiting, the page shows its message as a warning and keeps the note and selection.

#### Dead letter detail page

| Section | Shows |
|---|---|
| **Dead Letter Details** | Status, dead-lettered time, retry count, reason, resolution |
| **Manifest Details** | Manifest name, schedule, max retries, timeout, properties JSON masked as a work queue entry's input is |
| **Most Recent Failure** | The latest failed run's failure junction, exception, reason, stack trace and input |
| **Failed Execution History** | The manifest's failed runs, a page at a time, each linked to its run |

With the dead letter `AwaitingIntervention`:

| Action | Does |
|---|---|
| **Re-queue** | Creates a work queue entry from the manifest, marks the dead letter `Retried`, opens the new entry. The run replays the failed run's decisions when that is sound. |
| **Re-queue, Ask Afresh** | The same; the run asks its deciders afresh. |
| **Acknowledge** | Asks for a note of at most 1,000 characters and marks the dead letter `Acknowledged`. A refused note is shown and nothing changes. |

### Work queue

Entries waiting for dispatch.

| Column | Value |
|---|---|
| **Subject** | The entry's [subject key](/docs/mediator/queued-work#two-mutations-for-one-record), when the train sets one. Computed by your train, so it may carry record identifiers. |
| **Confirmed** | **Yes** for a confirmed entry; a **Staged** badge on a queued entry not yet confirmed, which the dispatcher will not claim; a dash for an unconfirmed entry no longer queued. |

#### Work queue entry detail page

Read through
[`IOperationsService.GetWorkQueueEntryDetailAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#read-models),
as `workQueue.detail`.

- **Waiting On**, when the subject blocks dispatch: "Entry N, which is running for the same
  subject", or "Entry N, which is ahead of it for the same subject" for a queued sibling, since
  dispatch takes only the first queued entry per subject each cycle. The sibling named is one
  dispatch would take first, by the same filters: a sibling not yet due, or in a disabled group, is
  not named.
- The input, with its [`[TraxSensitive]`](/docs/sdk-reference/configuration/save-train-parameters#masking-sensitive-fields)
  members written as `{"_redacted": true}`, by the rule the API applies to the same read (see
  [Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate)).
  The input is read back as the input type of the train registered on this host; when no train
  takes that type, or it does not read as it, the whole input is `{"_redacted": true}`.

### State machines

The instances of every persisted [state machine](/docs/statemachine), system-owned and users'
drafts alike. Read-only except for cancelling a system-owned instance's run.

- **Instances By State** counts instances by machine, state and owner (the system, or a user for a
  draft); a count opens those instances.
- The list filters by machine, state and owner, newest first by last write. Each row: machine,
  owner, id, state, created, updated, and **Waiting On A Run** when its state has
  [invoked a train](/docs/statemachine/invoking-trains). The total counts at most 10,000; the
  counts above are exact.
- An instance's page, at `/trax/data/state-machines/{machine}/{system|user}/{id}` (with
  `?row={rowId}` for a draft), lists the runs it invoked, newest first, up to 50, the one its
  state waits on marked **Live** (linked to its work queue entry while only queued). A draft lists
  only its live run.
- **Cancel**, on a system-owned instance waiting on a run, cancels that run after a confirmation,
  and the instance moves through the state's `OnCancelled` edge. It calls
  `CancelMachineInstanceAsync`, as [`cancelMachineInstance`](/docs/sdk-reference/graphql-api/mutations/machine-instances#cancelmachineinstance).
  Drafts offer no Cancel.

Neither page shows an instance's context or the user who owns a draft: the context is untyped
JSON that nothing could mask. Reads go through `GetMachineInstanceStateCountsAsync`,
`GetMachineInstancesAsync`, `CountMachineInstancesAsync`, `GetMachineInstanceAsync` and
`GetMachineInstanceRunsAsync`, the calls behind
[`machineInstanceCounts`, `machineInstances` and `machineInstance`](/docs/sdk-reference/graphql-api/queries/machine-instances#machineinstances).

### Persisted operations

Shown when the host registers `IPersistedOperationsService` and declares the management gate
(`GateManagement`). Rows are addressed by tenant and id, each at
`/trax/data/persisted-operations/{id}?tenant={key}` (no `tenant` for the default). The editor takes
a document of up to 1,048,576 characters. Each read and write checks the dashboard's posture and
then the management gate, so an operator who does not meet the gate is refused, as in GraphQL. What the page does and how to enable it is in
[Turn on persisted operations](/docs/api/turn-on-persisted-operations#7-manage-operations-from-the-dashboard).

### Batch actions

The Metadata, Work Queue, Dead Letters, Manifests and Manifest Groups grids let you tick rows and
act on them together. A selection holds row ids, so rows ticked on one page stay selected on
another.

| Page | Action | Calls |
|---|---|---|
| Metadata | **Cancel Selected** | `CancelExecutionsAsync` |
| Work Queue | **Cancel Selected** | `CancelWorkQueueEntriesAsync` |
| Manifests | **Enable Selected**, **Disable Selected** | `SetManifestsEnabledAsync` |
| Manifests | **Replay On Retry**, **Ask Afresh On Retry** | `SetManifestsReplayDecisionsOnRetryAsync` |
| Manifests | **Trigger Selected**, **Trigger Selected, Ask Afresh** | `TriggerManifestsAsync` |
| Manifest Groups | **Enable Selected**, **Disable Selected** | `SetManifestGroupsEnabledAsync` |
| Manifest Groups | **Enable All**, **Disable All** | `SetAllManifestGroupsEnabledAsync` |
| Manifest Groups | **Cancel Running** | `CancelManifestGroupsAsync` |
| Manifest Groups | **Trigger Selected** | `TriggerManifestGroupsAsync` |

All are on `IOperationsService`, as the API's [batch mutations](/docs/sdk-reference/scheduler-api/i-operations-service#batch-actions).
The page shows the service's message; the count includes only rows changed, and zero is a
warning. At most 1,000 ids per call; a larger or empty selection is refused and kept.

A trigger sends the whole selection in one call (see
[Batch triggers](/docs/sdk-reference/scheduler-api/i-operations-service#batch-triggers)), and shows
the service's line, such as "2 queued, 1 already queued (that entry now runs as the trigger) across
3 of 3 manifest(s).". On the groups page the counts are of the groups' manifests. Ids the service
could not trigger as asked are listed under the notification, which is then a warning. An accepted
trigger clears the selection; a refused one keeps it. **Ask Afresh** sends `askAfresh`; a retry the
dispatcher claimed first still replays, counted as "already dispatched and still replaying".

## Effects page

`/trax/settings/effects`, under **Settings**. Every registered effect and junction effect provider
factory, read and written through
[`IEffectSettingsService`](/docs/sdk-reference/scheduler-api/i-effect-settings-service), the service
behind the API's effects query and [`setEffectEnabled`](/docs/sdk-reference/graphql-api/mutations/effects#seteffectenabled).

| Action | Behaviour |
|---|---|
| Toggle, then **Save** | Applies only the toggles changed since the page loaded, each through `SetEffectEnabled`, then reads every effect's state again. Changes apply to the next train execution scope. A refused toggle is reported and the others still apply. |
| **Discard Changes** | Drops unsaved toggles and shows the current state. |
| Settings button | On a factory implementing `IConfigurableEffectProviderFactory<TConfiguration>`: opens a form of the configuration type's public read-write properties. |

In the settings form, a boolean is a switch, an enum a drop-down, and a number, string, date, time,
duration, `Guid` or `char` a text field read by the rules of the [run and queue dialogs](#run-and-queue-dialogs).
Any other property, such as a delegate, is listed as set in code or not set, and never written. A
blank field is `null` where null is accepted and refused otherwise; a value must pass the
property's `ValidationAttribute`s. A [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive)
setting is never shown: the form says whether one is set and offers a masked field, and a blank
field keeps the value. **Save** sends `ConfigureEffect` only the changed fields, all or none, and a
refusal keeps the dialog open. Settings are per process and not persisted.

## Server settings

`/trax/settings/server`. The scheduler's runtime settings and the host's log levels.

| Group | Settings |
|---|---|
| **Administrative Trains** | ManifestManager and JobDispatcher switches |
| **Polling & Queue** | Polling interval, `MaxActiveJobs`, the local worker count when local workers are registered |
| **Retry Settings** | `DefaultMaxRetries`, **Failure Count Window**, retry delay, backoff multiplier, maximum delay |
| **Job Settings** | `DefaultJobTimeout`, `StalePendingTimeout`, startup recovery |
| **Dead Letter Settings** | Dead letter settings |
| **Metadata Cleanup** | On a database provider, where [metadata cleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) always runs |

**Save** sends only the changed fields through `IOperationsService.UpdateSchedulerConfigAsync`, the
call behind [`updateScheduler`](/docs/sdk-reference/graphql-api/mutations/config),
then reloads the settings in force. A saved setting reaches every scheduler host and replaces the
value configured in code (see [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler#remarks));
there is no button to return to the code value. **Discard Changes** drops unsaved edits. A duration
outside the [range the service accepts](/docs/sdk-reference/scheduler-api/add-scheduler#value-ranges)
shows a message and disables **Save**.

**Logging** lists each category under `Logging:LogLevel`, `Default` first, with its level now, read
and saved through [`ILogLevelService`](/docs/sdk-reference/scheduler-api/i-log-level-service).
**Save** sends only changed levels, applied to this process's logger filters until it restarts;
other processes keep their own. When the host sets a category's filter itself after Trax, the page
names the categories whose saved level is not the one in force. Hidden when the host configures no
`Logging:LogLevel` category.

## User settings

`/trax/settings/user`. Stored in the browser's `localStorage`, for that browser only.

| Setting | Default | Description |
|---|---|---|
| **Polling Interval** | 5 seconds | How often pages re-query. 1 to 300 seconds. |
| **Hide Administration Trains** | `true` | Leaves ManifestManager, JobDispatcher, JobRunner, MetadataCleanup and DeadLetterCleanup out of statistics, charts, the Trains page and the Metadata and Manifests lists. Matches their names exactly, as the API's `hideAdminTrains` does. |
| **Dashboard Components** | All visible | Which home page panels show. |

## Layout and refresh failures

Built on [Radzen Blazor](https://blazor.radzen.com/) v11, with a sidebar and a light and dark theme
toggle kept in `localStorage`. When a refresh fails, the header shows **Last refresh failed**, with
the error as its tooltip, until one succeeds; the rows on screen are from the last refresh that
worked. A grid that loads page by page clears its rows and shows the error instead. A failed load
never ends the operator's connection. A custom
[IDashboardSettingsService](/docs/sdk-reference/dashboard-api/i-dashboard-settings-service) receives
failures through `NotifyPollFailed` and exposes the latest as `LastPollError`.
