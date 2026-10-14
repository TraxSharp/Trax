---
title: Recover a deciding train without asking the model again
description: "Set up a deciding train so that a retry after a crash replays the answers its model gave, instead of asking again and maybe taking another track."
type: how-to
area: effects
nav_order: 10
source:
  - Trax.Effect/src/Trax.Effect.Data/Decisions
  - Trax.Scheduler/src/Trax.Scheduler/Services/DecisionRecording/DecisionRecordingStartupCheck.cs
---

# Recover a deciding train without asking the model again

A run replays the decisions of another run only when it was queued to: by a manifest's automatic
retry, a requeue of the manifest's dead letter, or a requeue of an execution (`requeueExecution`,
the dashboard's **Re-queue**). A train run directly (`Run`, a `[TraxQuery]` or `[TraxMutation]` in
Run mode) or queued with `queueTrain` / `QueueTrainAsync` never replays, and neither does a
manifest's scheduled run that is not a retry. So a train whose crash should be recovered without
asking the model again runs from a manifest. The rules a replay follows are on
[Decision records](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions).

## 1. Record decisions on every host that runs the train

```csharp compile
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

services.AddTrax(trax => trax.AddEffects(effects => effects
    .UsePostgres(connectionString)
    .AddDecisionRecording(o => o.HashStatesWith(Convert.FromBase64String(configuration["StateHashKey"]!)))));
```

- Call `AddDecisionRecording()` after the data provider. Before one, it does not compile.
- Use Postgres or Sqlite, not InMemory. A retry is linked to the failed run by comparing the work
  queue entry's input, which the InMemory scheduler does not keep; on InMemory nothing fails, the
  retry asks afresh.
- Give the host a state hash key when a decision's state can reach a `[TraxSensitive]` member.
  Without one, such a decision is stored with no hash and never replays, and a warning names the
  state type once. Every process that may repeat a run uses the same key.
- Register any `IDecisionObserver` of your own before `AddTrax`. One registered after it replaces
  the composite recording depends on, and the host refuses to start, naming the observer.

A host that runs a deciding train without `AddDecisionRecording()` refuses to start and names the
trains ([A host that does not record](/docs/effect/decisions#a-host-that-does-not-record)).

The decider itself comes from the container or Memory as usual; without one, the startup check
refuses the chain. [Answer decisions with Nimble](/docs/effect/answer-decisions-with-nimble) sets
one up.

## 2. Run the train from a manifest with retries

```csharp compile
using Trax.Scheduler.Services.TraxScheduler;

var manifest = await scheduler.ScheduleOnceAsync<IUnderwriteLoanTrain, LoanApplication, LoanDecision>(
    new LoanApplication("a3", 700),
    TimeSpan.Zero,
    options => options.MaxRetries(3),
    ct);
```

`MaxRetries` must be 1 or more (the default is 3), and `ReplayDecisionsOnRetry` must stay on, which
is the default. Trax.Api has no GraphQL operation that creates a manifest, so a host whose callers
start such runs over GraphQL exposes its own: a `[TraxMutation]` train whose junction makes the
call above and returns the manifest's `Id`, which `operations.executions(manifestId:)` takes to
list each attempt. The [Recovery sample](/docs/samples/recovery) does this.

## 3. Keep the input and the state stable between attempts

- The manifest's input must be byte-identical between attempts. The scheduler links a retry only
  to a failed run queued with exactly the same input. Keep anything that varies between attempts,
  a fault-injection flag for instance, out of it.
- The state each decision is asked about must hash the same on the retry: no timestamp, random
  id, counter or cache in it. An answer whose state hashes differently is asked afresh, with the
  reason in `replay_refused`.

Trax has no per-junction retry. The retry runs the chain from its first junction, and only the
answers are replayed, so there is something to save only when the crash happens after the
decisions.

## 4. Check what the retry did

| Between the failure and the retry | The retry |
|---|---|
| Nothing changed | Is linked to the failed run (`replay_decisions_of`); every answer replays, `replayed` set, no decider asked |
| Data the state is built from changed | Is still linked; the replay refuses each answer whose state hashes differently, stores the reason in `replay_refused` and asks afresh. Not `replay_abandoned` |
| The manifest's input was edited | Is not linked: asks afresh |
| `triggerManifest(askAfresh: true)` before the dispatcher claimed the retry | Is not linked: asks afresh |
| The failed run was deleted, or the retry ran on a host without `AddDecisionRecording()` | Was linked, could not honour it: asks afresh and is marked `replay_abandoned` |
| The failed run had itself replayed another run | Is not linked: a retry replays at most once in a row |

To see why an answer was asked afresh, read the run's rows of `trax.decision`: `Replayed`, and in
the `Answer` JSON a `replay_refused` property with the reason. The run's
`Metadata.ReplayDecisionsOf` and `Metadata.ReplayAbandoned` say whether it was linked and whether
the link was abandoned. Neither GraphQL nor junction events carry these: a step says `replayed`,
not why it was not.

```csharp compile
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

await using var context = await dataContextFactory.CreateDbContextAsync(ct);
var rows = await context.RecordedDecisions.AsNoTracking()
    .Where(d => d.MetadataId == metadataId).OrderBy(d => d.Id).ToListAsync(ct);
var refused = rows.Select(d => d.Answer is null
    ? null
    : JsonNode.Parse(d.Answer)?["replay_refused"]?.GetValue<string>());
```

`dataContextFactory` is an `IDataContextProviderFactory` from the container.
[`IOperationsService.GetRecordedDecisionsAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#recorded-decisions)
reads a run's rows a page at a time, leaving out an answer to a `[TraxSensitive]` question and
every decision on a track taken on one.

## SDK Reference

> [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [ScheduleOnceAsync](/docs/sdk-reference/scheduler-api/manifest-management#scheduleonceasync) | [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service)
