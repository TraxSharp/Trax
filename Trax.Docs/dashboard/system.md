---
title: System
description: "Reference for the operations console's System area: hosts and heartbeats, the scheduler's switches and settings, log levels, effects, logs, live events."
type: reference
area: dashboard
nav_order: 8
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/ClusterPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/SettingsPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/EffectsSettingsPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/LogsPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/RealTimePage.tsx
---

# System

The deployment itself: the processes that run Trax, the scheduler's settings, the effects, and the
logs. **Bulk operations**, also under System, has [a page of its own](/docs/dashboard/bulk-operations).

## Hosts

`/system/hosts`, read through [`operations.hosts`](/docs/sdk-reference/graphql-api/queries/operations#hosts).
Every process that runs Trax writes a heartbeat every 15 seconds, with or without work. A host that
misses three beats (45 seconds) is offline; one silent for a day is no longer listed.

Above the table: how many hosts, how many are online, and how many runs are running now.

| Column | Value |
|---|---|
| **Host** | Its name, and the start of its instance id (the whole id on hover) |
| **Status** | Online or Offline |
| **Runs the** | Manifest manager, job dispatcher, both, or neither |
| **Worker slots busy** | How many of its local worker slots are in use, or "No local workers" |
| **Running now** | Its runs in progress, linking [Runs](/docs/dashboard/runs) filtered to In progress on that host |
| **Last heartbeat**, **Environment**, **Version** | When it last beat, its environment name, and the Trax version it runs |

The page says when the job dispatcher is paused, so no host starts queued work, with a link to the
Scheduler page, and when no online host runs the dispatcher at all.

**This process** shows the CPU, working set, GC heap and uptime of the process serving the console,
the only host whose memory the console can read.

## Scheduler

`/system/scheduler`. The scheduler's settings, stored in the database and shared by every scheduler
host, read through `operations.config` and saved through [`updateScheduler`](/docs/sdk-reference/graphql-api/mutations/config).
A saved change applies at once on the host serving the page and within a few seconds on every other
scheduler host, unless its section says otherwise. A saved setting replaces the value configured in
code ([AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler#remarks)).

**Pause and resume** has a switch for each of the scheduler's two loops, each behind a confirmation.
While either is paused, the header says so on every page.

| Loop | Does | Paused |
|---|---|---|
| **Manifest manager** | Queues each manifest's run when its schedule comes due | No manifest is queued on its schedule. Work queued by hand is still started. |
| **Job dispatcher** | Starts queued work on a worker | Queued work waits and nothing new starts from the queue. Runs already started finish. |

The other settings, a section each, each with its own **Save** and **Discard**. A section saves only
the fields changed in it.

| Section | Settings |
|---|---|
| **Polling and queue** | How often the manifest manager checks schedules and the job dispatcher checks the queue; max active jobs across all groups (blank for no limit); local workers per host (blank for one per processor, used from a host's next start) |
| **Retry** | Default max retries, default retry delay, retry backoff multiplier, max retry delay, failure count window |
| **Job timeouts** | Default job timeout, stale pending timeout, and failing runs left in progress when a host starts (applies at a host's next start) |
| **Retention and purge** | Deleting resolved dead letters automatically and how long to keep them; on a host with metadata cleanup, how often it runs and how long scheduler train runs are kept |

A duration outside the [range the service accepts](/docs/sdk-reference/scheduler-api/add-scheduler#value-ranges)
is refused with the service's message.

**Log levels** lists each category under `Logging:LogLevel` with its level now, read and saved
through [`ILogLevelService`](/docs/sdk-reference/scheduler-api/i-log-level-service). It is not
stored with the settings: a change applies at once, only in the process serving the console, until
it restarts. Scheduler and worker processes keep their own levels. A level set this way is marked
**Runtime override**.

## Effects

`/system/effects`. Every registered effect and junction effect provider factory, read and written
through [`IEffectSettingsService`](/docs/sdk-reference/scheduler-api/i-effect-settings-service), the
service behind the API's effects query and [`setEffectEnabled`](/docs/sdk-reference/graphql-api/mutations/effects#seteffectenabled).
The registry is per process: a change applies to the next run's scope in the process serving the
console and does not reach separate scheduler or worker processes.

| Action | Does |
|---|---|
| A row's switch, **Enable all**, **Disable all** | Marks effects on or off; an effect that cannot be turned off reads **Always on** |
| **Save** | Applies the changed switches, then reads every effect again. A refused switch is reported and the others still apply. |
| **Discard changes** | Drops unsaved switches |
| **Configure** | On a factory with a configuration type: a form of its settings |

In **Configure**, each setting is a field for its type. A setting set in code, such as a delegate, is
shown as set or not and is not editable. A [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive)
setting is never shown: its field opens blank, says whether a value is set, and is sent only when
given a new one. Save sends only the changed settings, all or none, and a refusal keeps the dialog
open with its reason.

## Logs

`/system/logs`. Every run's log entries, newest first, read through
[`operations.logs`](/docs/sdk-reference/graphql-api/queries/logs). Filters, kept in the address:
**Run** id, **Minimum level**, **Category contains**, **Message contains**, and **Order**. The text
filters ignore case; an exception's text cannot be searched. With a text filter the count stops at
10,000 and the pager says so. A run's own log is the **Logs** tab of [its page](/docs/dashboard/a-run#tabs).

## Live events

`/system/live`. What the server pushes to the console, from the moment the page opened: every run's
state change in an event stream, and a count of each change signal (work queue, dead letters,
manifests, manifest groups, scheduler settings) that makes the list pages read again. The header
says whether events are arriving.
