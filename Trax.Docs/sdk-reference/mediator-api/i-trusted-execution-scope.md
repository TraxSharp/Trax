---
layout: default
title: ITrustedExecutionScope
description: Reference for ITrustedExecutionScope, which marks an async flow as trusted infrastructure and switches off per-train authorization until disposed.
parent: Mediator API
grand_parent: SDK Reference
nav_order: 7
---

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible. See [API Security](/docs/api-security).

# ITrustedExecutionScope

Marks the current async flow as trusted infrastructure, which switches off per-train authorization for every train run or queued on that flow until the scope is disposed. It is the one way past the fail-closed [`[TraxAuthorize]`](/docs/sdk-reference/attributes/trax-authorize) check, so open it only around work that was already authorized somewhere else.

## Signature

```csharp
namespace Trax.Mediator.Services.TrustedExecution;

public interface ITrustedExecutionScope
{
    bool IsTrusted { get; }
    string? CurrentReason { get; }
    IDisposable BeginTrusted(string reason);
}
```

`AddMediator` registers the default implementation, `TrustedExecutionScope`, as a singleton. Resolve the interface; do not construct the class.

## Members

| Member | Description |
|--------|-------------|
| `BeginTrusted(string reason)` | Opens a trusted scope on the current async flow and returns the handle that closes it. `reason` is a short identifier, such as `"billing.nightly-import"`, written to the log when a train runs under trust. Throws `ArgumentException` when `reason` is null, empty or whitespace. |
| `IsTrusted` | True while a scope opened on this flow, or on the flow that started it, is still open |
| `CurrentReason` | The reason of the innermost open scope, or `null`. For logs only: never decide whether to trust from it. |

## What trust changes

Inside a scope:

- Trax.Api's `TrainAuthorizationService` returns without checking anything: no request, user, policy or role is needed. It logs the skip at `Information` with the reason.
- On a host with no `ITrainAuthorizationService`, a `[TraxAuthorize]` train is run or queued instead of being refused with `TrainAuthorizationNotConfiguredException`.

Nothing else changes. Train lookup, the input size cap, input validation, concurrency limits, `OnQueue` and `QueueSubjectKey` all apply as usual. A custom `ITrainAuthorizationService` is still called inside a scope; it must check `IsTrusted` itself to behave the same way.

## Scope rules

- **It follows the async flow.** State lives in a static `AsyncLocal`, shared by every instance in the process. It follows `await` and reaches tasks started inside the scope, and does not reach unrelated requests.
- **Scopes nest.** The inner reason wins. Disposing an outer scope while an inner one is open marks it closed without ending the inner one; when the inner one is disposed, the flow returns to the nearest scope still open, or to untrusted. A disposed scope never becomes current again.
- **Disposing twice is harmless.**
- Await what you start inside a scope, and dispose the handle on the flow that opened it.

## Who opens one

| Caller | Reason | Why it is trusted |
|--------|--------|-------------------|
| A runner's remote-run endpoint, serving `UseRemoteRun` requests | `scheduler.remote-run` | The work was authorized when it was submitted, and the runner's own posture (a signing key or a policy) guards the endpoint |
| The dashboard's queue, run and re-queue actions | `dashboard` | The host gates the whole dashboard |

The scheduler's own dispatch does not need one: it runs queued work through `ITrainBus`, which checks no authorization.

## Example

```csharp
using Trax.Mediator.Services.TrustedExecution;

public class NightlyImportJob(ITrustedExecutionScope trust, ITrainExecutionService trains)
{
    public async Task Run(string inputJson, CancellationToken ct)
    {
        using (trust.BeginTrusted("billing.nightly-import"))
        {
            await trains.QueueAsync("MyApp.Billing.IImportInvoicesTrain", inputJson, ct: ct);
        }
    }
}
```

Never open a scope around code that serves a caller the train's authorization is meant to check, and never decide to open one from anything the caller supplies. If you do, any caller can run any `[TraxAuthorize]` train.

See [Authorization: Fail-Closed Behavior](/docs/authorization#fail-closed-behavior).

## Package

```
dotnet add package Trax.Mediator
```
