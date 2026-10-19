---
title: Schedules
description: "Reference for the operations console's Schedules area: manifests and their next runs, a manifest's page, manifest groups and a group's settings."
type: reference
area: dashboard
nav_order: 7
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/ManifestsPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/ManifestDetailPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/ManifestGroupsPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/ManifestGroupDetailPage.tsx
---

# Schedules

[Manifests](/docs/scheduler) and manifest groups, as two tabs: **Manifests** at `/schedules` and
**Manifest groups** at `/schedules/groups`. Times of upcoming runs read in UTC, with how long until
each.

## Manifests

Read through [`operations.manifests`](/docs/sdk-reference/graphql-api/queries/manifests), with each
row's schedule in words and its next runs read for the same page.

| Column | Value |
|---|---|
| **Manifest** | Its train's short name, linking the manifest, and its external id |
| **Schedule** | The schedule in words, such as a cron expression read out; the expression itself on hover |
| **Next run** | How long until its next run, and when in UTC; the next three on hover. "Not scheduled" when nothing is due |
| **Enabled** | Enabled or Disabled |
| **Retries** | Replay or Ask afresh: what a retry of a failed run does with the decisions that run recorded |
| **Last success** | When it last completed |

Filters: name, schedule type, enabled, and **Hide scheduler trains**. Each row has **Queue** (after a
confirmation) and **Enable** or **Disable**.

| With rows ticked | Does |
|---|---|
| **Queue selected**, **Queue selected, ask afresh** | Triggers the ticked manifests in one call to `triggerManifests`. The notice under the bar counts what was queued, what already had a queued run (which then runs as the trigger) and what could not be triggered, by id. A refused batch keeps the selection. |
| **Enable selected**, **Disable selected** | `setManifestsEnabled` |
| **Replay on retry**, **Ask afresh on retry** | `setManifestsReplayDecisionsOnRetry`; offered for trains that ask a decider |
| **Queue all N matching**, **Enable all N matching**, **Disable all N matching** | A [bulk operation](/docs/dashboard/bulk-operations) over every manifest the filter matches |

The id-list actions take at most 1,000 ids a call.

## A manifest

`/schedules/{id}`. Its train's name, Enabled or Disabled, and a **Runs on its schedule** switch that
enables or disables it (disabling asks first).

| Part | Shows |
|---|---|
| Counts | Total runs, completed, failed, in progress |
| **Schedule** | The schedule in words and its next runs, each in UTC with how long until it. A disabled manifest, or one that runs only when triggered or after what it depends on, says it has no upcoming run. |
| **Run history** | Completed, failed and cancelled runs over the last 24 hours or 7 days, with totals |
| **Details** | Id, external id, name, schedule type, cron, interval, variance, scheduled at, next scheduled run, misfire policy and threshold, max retries, timeout, priority, group, the manifest it depends on, last success, links to its queue entries and its dead letters, and a **Replay decisions on retry** switch for a train that asks a decider |
| **Properties** | The input it runs with, as the API returns it |
| **Exclusion windows** | The days, dates, ranges and daily times it skips ([exclusions](/docs/scheduler/exclusions)) |
| **Recent runs** | Its latest runs, each linking its run |

| Action | Does |
|---|---|
| **Queue now**, **Queue now, ask afresh** | Triggers it now. The message says whether a new run was queued, the queued run was brought forward, or one already due was released as the trigger. |
| **Queue later** | Asks for a number of minutes and triggers it that far ahead |
| **Edit** | Changes its schedule type, cron expression or interval, max retries, priority and timeout |
| **Cancel runs** | Cancels every running run of the manifest, after a confirmation |

## Manifest groups

Read through [`operations.manifestGroups`](/docs/sdk-reference/graphql-api/queries/manifest-groups).

| Column | Value |
|---|---|
| **Group** | Its name, linking its page |
| **Manifests**, **Runs**, **Failed** | Its counts |
| **Max active jobs in group** | How many of its runs may be active at once; ∞ for no group limit |
| **Priority** | Its dispatch priority, 0 to 31 |
| **Enabled** | Enabled or Disabled |

**Enable all** and **Disable all** act on every group. With rows ticked: **Queue selected** (the
notice counts the groups' manifests), **Enable selected**, **Disable selected**, **Cancel running**.

**Dependency graph**, under the list, draws the groups with an arrow into each group from the groups
it runs after, each coloured by whether it is enabled, has a run in progress, or its latest run
failed. Clicking a group opens it. [Dependent trains](/docs/scheduler/dependent-trains) explains the
dependencies.

## A group

`/schedules/groups/{id}`. Its counts (manifests, total runs, completed, failed, in progress), its run
history, its dependencies across groups drawn around it, its manifests and its recent runs.

**Settings** edits **Max active jobs in this group** (blank for no group limit), **Priority** and
**Enabled**; **Save** sends them, and they apply from the dispatcher's next cycle. **Queue group**
triggers every manifest in it, and **Cancel group** cancels its running runs, each after a
confirmation.
