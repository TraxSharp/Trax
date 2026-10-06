---
layout: default
title: TraxRemote
description: Reference for TraxRemote, which sends a train's queued runs to a remote worker, and the order in which the scheduler picks a train's job submitter.
parent: Attributes
grand_parent: SDK Reference
nav_order: 4
---

# TraxRemote

Sends a train's queued runs to a remote worker instead of the scheduler host's local workers, without naming it in the scheduler's routing.

## Signature

```csharp
namespace Trax.Effect.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxRemoteAttribute : Attribute
{
    public TraxRemoteAttribute();
}
```

Place it on the concrete train class (or a base class). It is not read from an interface.

## Routing

The scheduler picks a train's job submitter in this order:

1. A `ForTrain<T>()` route on a [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers), [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) or [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) call.
2. `[TraxRemote]`: the **first** of those calls in the builder, of whatever kind.
3. The default local submitter.

A scheduler with a `[TraxRemote]` train and none of those three calls fails when it is built, naming each such train. A train marked remote, often to keep it off the scheduler host, does not quietly fall back to running there.

The attribute routes queued work, which is what the scheduler dispatches. A direct run (`run*` mutations, `ITrainExecutionService.RunAsync`) goes through the `IRunExecutor` instead; see [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run).

## Example

```csharp
using Trax.Effect.Attributes;

[TraxRemote]
public class RenderReportTrain : ServiceTrain<RenderReportInput, Unit>, IRenderReportTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<RenderPdf>().Chain<UploadPdf>().Resolve();
}

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseRemoteWorkers(remote => remote.BaseUrl = "https://workers.internal/trax/execute")));
```

See [Remote Execution](/docs/scheduler/remote-execution) for the worker side.

## Package

```
dotnet add package Trax.Effect
```
