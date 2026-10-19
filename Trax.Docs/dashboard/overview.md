---
title: Overview
description: "Reference for the operations console's Overview page: the run totals, health, host process and charts it shows, and the panels Preferences can hide."
type: reference
area: dashboard
nav_order: 2
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/OverviewPage.tsx
---

# Overview

The console's home page, at the console's root path. It reads
[`operations.metrics.dashboard`](/docs/sdk-reference/graphql-api/queries/metrics#dashboard) and
`operations.health`, and follows run events live. The scheduler's own trains are left out while
**Hide scheduler trains** is on ([Preferences](/docs/dashboard/preferences)), and each panel can be
hidden there.

| Panel | Shows |
|---|---|
| **Summary cards** | Runs started today (UTC, any state), success rate, runs running now, unresolved dead letters. Success rate is completed over completed plus failed; cancelled runs do not count. |
| **Health** | Queue depth, runs in progress, failures in the last hour, dead letters, and the health status beside the page title. |
| **Server** | Uptime, working set, GC heap and CPU of the process serving the console, not of the workers. |
| **Runs over time** | Completed, failed and cancelled runs per hour over the last 24 hours, or per minute over the last hour (the **24h** and **1h** buttons), and **Throughput by train (7d)**. |
| **Top failures (7d)** | The trains with the most failed runs. |
| **Slowest trains (7d)** | The trains with the longest average completed run. |

The Overview is being reworked to lead with what needs attention, each entry a link to the
filtered list behind it, and to move the host process's figures to [System](/docs/dashboard/system#hosts).
Until that lands, the panels above are what it shows.
