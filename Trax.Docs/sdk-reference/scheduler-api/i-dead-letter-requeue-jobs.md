---
layout: default
title: IDeadLetterRequeueJobs
description: "Reference for IDeadLetterRequeueJobs, which requeues every dead letter awaiting intervention as a background job on this node and reads the job back by id."
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 17
---

# IDeadLetterRequeueJobs

Requeues every dead letter awaiting intervention as a background job on this node, and reads the job back by id. The dashboard's **Requeue All** and the GraphQL API's [`requeueAllDeadLetters`](/docs/sdk-reference/graphql-api/mutations#deadletters-nested-namespace) both start the job here, so the fold over a large backlog (about 36 s at 500,000 dead letters) is bound by neither a request nor an operator's dashboard connection. Registered as a singleton by `AddScheduler(...)`.

## Signature

```csharp
namespace Trax.Scheduler.Services.DeadLetterRequeue;

public interface IDeadLetterRequeueJobs
{
    Task<DeadLetterRequeueJob> StartAsync(bool askAfresh = false, CancellationToken ct = default);
    DeadLetterRequeueJob? Get(Guid id);
}

public record DeadLetterRequeueJob(
    Guid Id,
    DeadLetterRequeueJobStatus Status,
    int AwaitingAtStart,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int? Count,
    string Message,
    bool AskAfresh
)
{
    public bool Started { get; init; } = true;
    public int Processed { get; init; }
}

public enum DeadLetterRequeueJobStatus { Running, Succeeded, Failed, Canceled }
```

## StartAsync

Counts the dead letters awaiting intervention, starts the fold over them in the background and returns the job at once, `Running`, with `AwaitingAtStart` set and the message `Requeueing {n} dead letter(s) awaiting intervention.` The fold is [`ITraxScheduler.RequeueAllDeadLettersAsync`](/docs/sdk-reference/scheduler-api/i-trax-scheduler), resolved from a scope of its own, so it creates at most one work queue entry per manifest and commits a page of manifests at a time. `askAfresh: true` calls its `askAfresh` overload, so every new run asks its deciders afresh rather than replaying the failed run's decisions.

While it runs, the fold reports after each page, and the job's `Processed` and message follow it: `Requeued {processed} of {awaitingAtStart} dead letter(s) awaiting intervention so far.` `Processed` can pass `AwaitingAtStart` when dead letters arrive while the job runs; once it has succeeded it equals `Count`. The dashboard draws it as a bar and the API returns it as `processed`.

`ct` cancels the count only. The fold runs under the host's `IHostApplicationLifetime.ApplicationStopping` token, never the caller's, so a caller that goes away does not stop it; the host shutting down stops it between pages.

One fold runs per node at a time. While one is running, `StartAsync` starts nothing and returns the running job with `Started = false`. When the running job is in the other mode (it replays and this call asks afresh, or the reverse), the returned copy's message also says this request was not started: `A requeue-all that {asks its deciders afresh | replays recorded decisions} is already running on this node, so this one was not started. Read requeueAllJob(id) until it finishes, then ask again.`

## Get

The job with this id, or `null` when this node does not know it: it was started on another node, this node has restarted since, or it finished more than 24 hours ago. A node keeps at most 100 finished jobs, forgetting the oldest first; a running job is never forgotten.

## How a job ends

| `Status` | `Count` | `Message` |
|---|---|---|
| `Succeeded` | How many dead letters it requeued | The scheduler's own message, which also counts the folded and skipped ones |
| `Failed` | `null` | `The requeue stopped on a server failure; the server's log has the detail. Dead letters it requeued stay requeued; requeueAllDeadLetters again requeues the rest.` The exception is logged, never put in the message |
| `Canceled` | `null` | `The requeue stopped because the server was shutting down. Dead letters it requeued stay requeued; requeueAllDeadLetters again requeues the rest.` |

The messages are the API's, word for word, because both surfaces show the same job. `DeadLetterRequeueJobs` exposes them as `FailedMessage`, `CanceledMessage` and `OtherModeMessage(bool)`, with `Retention` (24 hours) and `MaxRetained` (100).

## Jobs live in the node's memory

Another node does not know a job, and a restart forgets it. Nothing is lost by that: a requeued dead letter no longer awaits intervention, so a fold that stopped part-way left what it finished requeued, and starting another requeues the rest. Two folds on two nodes are safe together, since the scheduler retries a page that loses the race for a manifest's queued entry. Why the state is not in a table is recorded in [Trax.Api ADR 0036](https://github.com/TraxSharp/Trax.Api/blob/main/docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md).

## Package

```
dotnet add package Trax.Scheduler
```
