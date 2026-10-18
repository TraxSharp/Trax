---
title: "9.1 A schedule is a manifest"
description: "Chapter 9 of the Trax book, section 1: A schedule is a manifest."
type: tutorial
area: general
parent: "9. Scheduled work"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/the-operations-console/test-it
---

# 9.1 A schedule is a manifest

The scheduler runs trains on a schedule. Each scheduled train is a **manifest**: the train, the
input it runs with, when it runs, and what to do when it fails. A manifest is identified by an
external id that the host chooses, so the same manifest is found again on every start and by every
process that shares the database, however many times the host is deployed.

Those ids are written once, as constants, at the root of the project.

`ManifestNames.cs`:

```csharp compile=app file=ManifestNames.cs
namespace Shopfront;

// The external id of each manifest Shopfront schedules: its identity on every start and every host.
public static class ManifestNames
{
    public const string StockReport = "stock-report";
    public const string LowStockCheck = "low-stock-check";
}
```

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)

---

Next: [9.2 The stock report](/docs/book/scheduled-work/the-stock-report)
