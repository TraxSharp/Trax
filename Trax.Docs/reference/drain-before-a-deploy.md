---
title: Drain before a deploy
description: "What a drain is, what trax deploy does with it before it replaces the hosts, and how an operator drains, resumes and gets a refused deploy moving."
type: explanation
area: tooling
nav_order: 7
---

# Drain before a deploy

Replacing a host fails whatever it was in the middle of. Worse, a scheduler host that starts fails every run that was
InProgress before it started, on any host, because nothing records which host owns a run. A deploy that starts the new
hub while the old one still runs a job would fail that job.

So `trax deploy` drains first. A drain is one row in the database, `trax.drain`, that every host honours: the job
dispatcher dispatches nothing, the manifest manager queues no scheduled run, and no worker claims a job. What is already
running carries on. Once nothing is busy, nothing is live for the new hub's startup to fail, and the deploy replaces the
hosts, the old hub and the new one overlapping for a moment. The deploy ends the drain once every service has rolled.

## What a deploy does

On every target (compose, the podman runtime, Kubernetes and ECS) the order is the same:

1. Migrate the database (`--trax-migrate`), as before.
2. Drain: run the new version's image once with `--trax-drain <timeout>`. It sets the drain, then waits for the busy
   list to empty, up to the environment's `drainTimeout` (10 minutes unless `trax.deploy.json` says otherwise).
3. Roll the services. A new hub that starts while drained leaves the drain in place and fails no run at startup.
4. Resume: once every service has rolled, run the image once more with `--trax-resume`, which ends the drain. Until
   then a host the roll has not reached yet still starts nothing.

A first deploy, with nothing running yet, skips the drain on the targets that can tell.

The drain process runs inside the cluster, the ECS network or the compose project, and reaches only the database, so
the deploy needs no endpoint into the app. See [TraxDrain](/docs/sdk-reference/configuration/trax-drain) for the
arguments.

## What counts as busy

- A run that is Pending or InProgress. A Pending run whose job is still in the queue and no worker has claimed is not
  busy: it waits in the database and the next hosts run it. The scheduler's own trains are never busy.
- A state-machine instance that is not at rest: an effect is in flight on it, or the run it invoked is busy.

Queued work, triggers and requeues are accepted while drained and wait in the queue. A train a request runs inline
still runs, and the drain waits for it like any other run.

## Schedules while drained

A schedule that comes due while drained is not queued. When the drain ends it is a misfire, and the manifest's misfire
policy decides: `FireOnceNow` runs it once, `DoNothing` skips it if it is later than its threshold. There is nothing
new to configure.

## When the deploy refuses

Past the timeout with work still busy, the drain process ends the drain and exits non-zero, and the deploy stops before
it replaces anything. Its message lists what is still busy, the first 50 items with how many more:

```
Still busy after the drain's timeout of 00:10:00, so the drain was ended and nothing should be replaced. ...
  run 4211 MyApp.IExportTrain (InProgress, since 2026-11-24 14:02:11Z, on api-7c9f)
  instance checkout/3f1c... checkout (Charging, effect Charge in flight, since 2026-11-24 14:05:40Z)
```

Then either let the work finish and deploy again, cancel it from the console, or deploy with `--force`, which rolls
with the work still running. A forced roll keeps the drain until the resume, so the new hub's startup does not fail
what the old hosts still run; a run its host's stop cuts off is failed by the stale-run reaper once
`StaleInProgressTimeout` passes.

## When a deploy dies

A deploy that is interrupted or crashes after draining leaves a drain that lapses on its own. Once the drain has
finished waiting it holds for as long as the roll, and a rollback the deploy drives, can take: on Kubernetes ten
minutes per Deployment for each, plus ten; on ECS an hour, since the circuit breaker's rollback runs inside the stack
update; on compose and podman ten minutes. A drain that never finished waiting lapses at its timeout plus that. Until
then the hosts start nothing new; Resume in the console ends it at once. A scheduler host that starts meanwhile leaves
it in place.

## From the console or the API

System, Scheduler has Drain and Resume. Drain asks for how long, 20 minutes by default and at most a day, and records
who set it. The API has the same three fields under `operations`, behind the same operator gate:

```graphql
query { operations { config { drain { drained until startedBy busyTotal busy { kind id name state } } } } }
mutation { operations { config { drain(minutes: 30) { success message } } } }
mutation { operations { config { resume { success count message } } } }
```

Draining by hand is for the same reason a deploy does it: before you stop or replace a host yourself.

## Two hubs at once

During a roll the new hub and the old one both run for a moment. That is safe once drained: work is claimed with
`FOR UPDATE SKIP LOCKED` and the manifest manager holds an advisory lock, so two hubs never take the same run or queue
the same schedule twice, and the one hazard, the new hub's startup failing runs that are live on the old one, does not
arise: a scheduler host that starts while drained skips its stuck-run recovery. In steady state a hub still runs as one copy, because a copy that restarts on its own, with no drain
before it, would fail the other's live runs.

## SDK Reference

> [TraxDrain](/docs/sdk-reference/configuration/trax-drain) | [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service) | [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate)
