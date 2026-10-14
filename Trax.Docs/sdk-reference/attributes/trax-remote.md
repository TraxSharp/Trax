---
title: TraxRemote
description: Reference for TraxRemote, which sends a train's queued runs to a remote worker, and the order in which the scheduler picks a train's job submitter.
type: reference
area: scheduling
package: Trax.Effect
namespace: Trax.Effect.Attributes
source:
  - Trax.Effect/src/Trax.Effect/Attributes/TraxRemoteAttribute.cs
parent: Attributes
grand_parent: SDK Reference
nav_order: 4
---

# TraxRemote

Sends a train's queued runs to a remote worker instead of the scheduler host's local workers, without naming it in the scheduler's routing.

## Signature

```csharp fragment
namespace Trax.Effect.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxRemoteAttribute : Attribute
{
    public TraxRemoteAttribute();
}
```

## Behavior

The attribute is read from the concrete train class or a base class, not from an interface.

The scheduler picks a train's job submitter in this order:

1. A `ForTrain<T>()` route on a [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers), [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) or [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) call.
2. `[TraxRemote]`: the **first** of those calls in the builder, of whatever kind.
3. The default local submitter.

A scheduler with a `[TraxRemote]` train and none of those three calls fails when it is built, naming each such train. A train marked remote, often to keep it off the scheduler host, does not quietly fall back to running there.

The attribute routes queued work, which is what the scheduler dispatches. A direct run (`run*` mutations, `ITrainExecutionService.RunAsync`) goes through the `IRunExecutor` instead; see [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run).

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

[TraxRemote]
public class RenderReportTrain : ServiceTrain<RenderReportInput, Unit>, IRenderReportTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<RenderPdf>().Chain<UploadPdf>().Resolve();
}
```

The scheduler host that sends it to remote workers:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseRemoteWorkers(remote => remote.BaseUrl = "https://workers.internal/trax/execute")));
```

## See also

- [Remote Execution](/docs/scheduler/remote-execution), the worker side
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers)

