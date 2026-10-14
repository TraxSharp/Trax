---
title: Failure classification
description: "How a failed run gets a FailureClass: the IFailureClassifier a host registers, where it runs, what it never sees, and how a class travels from a remote worker."
type: explanation
area: effects
nav_order: 30
source:
  - Trax.Effect/src/Trax.Effect/Services/FailureClassifier/IFailureClassifier.cs
  - Trax.Effect/src/Trax.Effect/Extensions/ServiceTrainExtensions.cs
  - Trax.Core/src/Trax.Core/Exceptions/FailureClass.cs
---

# Failure classification

`OnFailed` hands a train an `Exception`. Deciding from it whether to retry, rebuild against current
state, or give up usually means matching on a message, and doing that at every call site is how a
vendor's wording change breaks three things at once. Trax supplies a vocabulary,
`Unclassified | Transient | Conflict | Permanent`, and asks one `IFailureClassifier` the host
registers which of them a failure is. Only the host knows what its remote systems' errors mean, so
the matching lives in one place that it owns.

```csharp compile
using System.Net;
using Trax.Core.Exceptions;
using Trax.Effect.Services.FailureClassifier;

public class LedgerFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Conflict } => FailureClass.Conflict,
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
                FailureClass.Transient,
            _ => null,
        };
}
```

It is an ordinary service (`services.AddSingleton<IFailureClassifier, LedgerFailureClassifier>()`).
The train resolves one from its service provider, so when several are registered the container's
last registration is the one asked. See [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier).

The answer is recorded on the run as `metadata.FailureClass`, readable in `OnFailed` and persisted,
so "how many conflicts this hour" is a query rather than a log trawl. Nothing in the scheduler acts
on it: a manifest's retries are counted, whatever the class. Adding a classifier changes what is
recorded, not what happens.

## Where it runs

The classifier runs where the failure happened, holding the original exception. Junctions enrich an
exception and return it rather than wrapping it, so the classifier can type-check and read
structured error data instead of parsing text. A failure raised outside any junction is classified
too, and its class is attached to the exception, so it travels with the failure when the run is
reported somewhere else.

A `Parallel` step's failure is classified branch by branch, each as it would be had it been the
run's failure, and the classes are combined: the step can be retried as a whole only when every
branch's failure can.

## What it does not decide

**No classifier, or no answer.** With none registered, or when it returns null, the failure records
`Unclassified`, which means "decide as you did before this existed". A value outside the enum is
recorded as `Unclassified` too.

**A classifier that throws.** Its exception is logged and the failure records `Unclassified`. A
classifier must never be able to mask the failure it was asked about.

**A requested cancellation.** A run whose own token is cancelled, or whose persisted cancel flag is
set (the dashboard's cancel button, the scheduler's job timeout), records `Cancelled`. It is not a
failure and the classifier never sees it.

**A cancellation nothing asked for.** An `HttpClient` timeout surfaces as `TaskCanceledException`
without the run's token being cancelled, so the run records `Failed` and the classifier is asked.
When it has no answer (null or `Unclassified`), Trax records `Transient`. A manifest retries only a
`Failed` run, so such a timeout is retried.

**A class the failure already carries.** That wins over the local classifier. It is the case for a
failure from a remote worker, for one a calling-side junction preserved when it enriched the
exception, and for some failures Trax raises itself, such as a decision a train cannot act on. A
class is read from an exception's message only when its type is exactly `TrainException`, the type
Trax rebuilds a serialized failure as. A type derived from it is treated like any other exception:
its message is often text from somewhere else, such as a remote system's response body, and must
not choose whether a failure is retried.

**Failures the scheduler records itself**, such as a dispatch failure or a run failed by the
stale-run reaper, record `Unclassified`: no train saw an exception to classify.

## Remote runs

A run executed by a remote worker is classified by the worker, which holds the real exception, and
the answer travels back with the failure. The classifier has to be registered in the worker
process: one registered only on the calling side is never asked about a remote run. The calling
side records what it was told, over HTTP or Lambda, and never passes the rebuilt exception to its
own classifier, because the original type is gone and a catch-all classifier would stamp a class on
something the worker deliberately left alone.

A worker that sends no class records `Unclassified`, and the calling side does not fill the gap. A
class the calling side does not know, sent by a newer worker, records `Unclassified` and keeps the
worker's error. Work sent through a job submitter has nothing to carry: the worker is given the
metadata id and writes to that row itself.

`Trax.Docs/adr/0020` records the reasoning.

## See also

- [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier)
- [Metadata](/docs/effect/metadata)
- [Remote execution](/docs/scheduler/remote-execution)

## SDK Reference

> [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train)
