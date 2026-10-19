---
title: Operations console
description: "The Trax operations console, the React app a Trax.Api host serves: how it is organised, the words it uses, and a page per area it covers."
type: landing
area: dashboard
section: Reference
nav_order: 1
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/AppRoutes.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/Layout.tsx
redirect_from:
  - /docs/dashboard/pages
---

# Operations console

The operations console is the Trax dashboard: a single-page app, shipped in the `Trax.Api.Dashboard`
package, that a Trax.Api host serves at `/trax/dashboard`. It shows the runs, trains, state machines
and schedules of a deployment, and takes the actions an operator needs on them: requeue a failed run,
cancel one, pause the scheduler, acknowledge a dead letter. Everything it shows or changes is a
request to the host's GraphQL `operations` fields, so it sees and refuses exactly what the API does.
A demo runs on recorded data at [traxsharp.net/dashboard](https://traxsharp.net/dashboard).

Setting it up is in [Add the console to a host](/docs/dashboard/add-the-console-to-a-host).

## How it is organised

The sidebar follows what an operator works with, not the tables behind it. Each entry is a page of
these docs.

| Entry | Holds | Page |
|---|---|---|
| **Overview** | Totals and charts for the whole deployment | [Overview](/docs/dashboard/overview) |
| **Runs** | Every run, with the views All, Failed, Queued and Dead letters | [Runs](/docs/dashboard/runs), and [A run](/docs/dashboard/a-run) for one run |
| **Trains** | Every registered train; a train's page holds its chain, recent runs, manifests and the machines that start it | [Trains](/docs/dashboard/trains) |
| **Machines** | Every registered state machine; a machine's page holds its diagram and its instances | [Machines](/docs/dashboard/machines) |
| **Schedules** | Manifests and manifest groups | [Schedules](/docs/dashboard/schedules) |
| **System** | Hosts, the scheduler's switches and settings, effects, logs, live events, bulk operations | [System](/docs/dashboard/system), [Bulk operations](/docs/dashboard/bulk-operations) |
| **API admin** | Persisted operations, shown only to a caller the host lets manage them | [API admin](/docs/dashboard/api-admin) |
| **Preferences** | What this browser keeps for itself, at the foot of the sidebar | [Preferences](/docs/dashboard/preferences) |

The header carries the search box on every page; see [Search](/docs/dashboard/search). It also
shows the host's environment name, a link to the Scheduler page while the manifest manager or the
job dispatcher is paused, **Last refresh failed** while the latest read is failing (its tooltip is
the error), the time to the next refresh when one is set in Preferences, and the time in UTC.

A route the console had before it was organised this way, such as `/executions/42` or
`/settings/server`, redirects to where the page lives now, so a link pasted into a ticket keeps
working.

## The words it uses

The console keeps Trax's own names: a manifest is a manifest, a junction is a junction. A heading
that leads with one carries a **?** beside it; hovering says what the term means in one sentence,
and clicking opens the term's entry in the [Glossary](/docs/reference/glossary).

- **A run is a run**: one execution of a train. The console never calls it an execution, a job or
  metadata.
- **One verb per action, the same on every page.** **Run** starts a train now and skips the queue.
  **Queue** adds work to the work queue. **Requeue** repeats a run, or a dead letter's manifest.
  **Resume** continues a run after a checkpoint. **Pause** and **Resume** on the Scheduler page
  apply to the scheduler. **Cancel**, **Acknowledge**, **Enable** and **Disable** mean what they say.
- **Ask afresh** is an option of Requeue and Queue: the new run asks its deciders again instead of
  replaying the answers an earlier run recorded. It is offered only for a train that asks a decider,
  and wherever the console cannot tell.
- An enum value reads by its display name: "Awaiting intervention", not `AwaitingIntervention`.

## What every page shares

- **Every id is a link.** A run's manifest, parent, queue entry and dead letter, a dead letter's
  manifest, an instance's waiting run: each opens its own page.
- **Filters live in the address.** A filtered list is a URL (`/runs?state=FAILED&train=...`), so it
  can be bookmarked, shared, or opened from another page. The train page's **All runs of this
  train** and a host's running count are such links.
- **Wide actions ask first.** Every cancel, requeue, acknowledge and pause opens a confirmation in the
  page. An action over a whole list, such as **Requeue all** or a [bulk operation](/docs/dashboard/bulk-operations),
  asks for a word or the matched count to be typed before its button enables.
- **Lists update live.** The console subscribes to the host's change signals and run events, and
  reads a list again when its rows change. A fixed refresh interval is optional, in Preferences.
- **Empty states say what fills the page** and link to the docs page that shows how.

## Pages

**How-to guides**

- [Add the console to a host](/docs/dashboard/add-the-console-to-a-host)

**Reference**

- [Overview](/docs/dashboard/overview)
- [Runs](/docs/dashboard/runs)
- [A run](/docs/dashboard/a-run)
- [Trains](/docs/dashboard/trains)
- [Machines](/docs/dashboard/machines)
- [Schedules](/docs/dashboard/schedules)
- [System](/docs/dashboard/system)
- [Bulk operations](/docs/dashboard/bulk-operations)
- [API admin](/docs/dashboard/api-admin)
- [Preferences](/docs/dashboard/preferences)
- [Search](/docs/dashboard/search)
- [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard), the options the
  host serves it with
