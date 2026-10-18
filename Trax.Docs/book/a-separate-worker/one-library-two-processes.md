---
title: "11.1 One library, two processes"
description: "Chapter 11 of the Trax book, section 1: One library, two processes."
type: tutorial
area: general
parent: "11. A separate worker"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/failures-retries-and-dead-letters/test-it
---

# 11.1 One library, two processes

Two processes that run the same trains need the same trains. They move into a class library,
`Shopfront.Core`, and each process becomes a thin executable that references it and says how it
runs: the **hub** serves the API and the console and decides what is due; the **worker** runs it.

```text
Shopfront/
├── Directory.Packages.props
├── Shopfront.slnx
├── Shopfront.Core/          the trains, their data, the shop: everything both processes run
├── Shopfront.Hub/           the API, the console and the scheduler
├── Shopfront.Worker/        runs queued and scheduled work
└── tests/
    └── Shopfront.Tests/
```

Move the code, from the solution's folder:

```bash
mkdir -p Shopfront.Core Shopfront.Hub/Auth Shopfront.Worker
mv Auth/DevJwt.cs Shopfront.Hub/Auth/
mv Auth Data Extensions Failures Fraud Inventory Payments Staff Suppliers Trains ManifestNames.cs Shopfront.Core/
mv Program.cs appsettings.json Properties Shopfront.Hub/
mv Shopfront.csproj Shopfront.Hub/Shopfront.Hub.csproj
```

The development signing key goes with the hub, which is the only process that signs anyone in.
Everything else the trains need goes to the library. The namespaces stay as they are: the library's
root namespace is `Shopfront`, so nothing that moved needs editing.

## SDK Reference

> [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Queued work](/docs/mediator/queued-work) | [Job submission](/docs/scheduler/job-submission) | [Structuring your application](/docs/reference/project-layout) | [Host tracking](/docs/effect/host-tracking)

---

Next: [11.2 The projects](/docs/book/a-separate-worker/the-projects)
