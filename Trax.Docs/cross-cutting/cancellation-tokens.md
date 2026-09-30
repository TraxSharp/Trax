---
layout: default
title: Cancellation Tokens
parent: Cross-Cutting
nav_order: 1
---

# Cancellation Tokens

Trax.Core threads `CancellationToken` through the entire pipeline, from the initial `Run` call, through every junction, down to EF Core queries and background service shutdown. This enables graceful cancellation of trains in response to HTTP request aborts, application shutdown, or explicit user cancellation.

## How It Works

CancellationToken propagation is **property-based**, not parameter-based. The token is stored as a property on `Train` and `Junction`, so the `Junction.Run(TIn input)` signature stays unchanged:

```
Run(input, cancellationToken)
        │
        ▼
┌──────────────────────────┐
│      Train                │
│  CancellationToken = ct  │
└──────────┬───────────────┘
           │  Chain(junction)
           ▼
┌──────────────────────────┐
│      Junction              │
│  CancellationToken = ct  │  ← set automatically before Run() is called
│  Run(input)               │  ← your code accesses this.CancellationToken
└──────────────────────────┘
```

1. The caller passes a `CancellationToken` to `train.Run(input, cancellationToken)`
2. The train stores it on its `CancellationToken` property
3. Before each junction executes, `RailwayJunction` copies the token from the train to the junction
4. The junction checks `CancellationToken.ThrowIfCancellationRequested()` before `Run()` is called
5. Inside `Run()`, your code accesses `this.CancellationToken` for async operations

## Passing a Token to a Train

`Run` takes the token:

```csharp
// Throws on failure, and on cancellation
await train.Run(input, cancellationToken);
```

`RunEither(input)` has no token parameter, and `Train.CancellationToken` cannot be set from outside the train. A train that a host runs (the mediator, the scheduler, a `ServiceTrain` resolved from the container) gets its token from the host.

If you call `Run(input)` without a token, `CancellationToken` defaults to `CancellationToken.None` and all existing code works unchanged.

## Using the Token Inside Junctions

Access `this.CancellationToken` in your junction's `Run` method. It is set automatically before `Run` is called, so you never need to set it yourself:

```csharp
public class FetchDataJunction(IHttpClientFactory httpFactory) : Junction<FetchRequest, ApiResponse>
{
    public override async Task<ApiResponse> Run(FetchRequest input)
    {
        var client = httpFactory.CreateClient();

        // Pass the token to async operations
        var response = await client.GetAsync(input.Url, CancellationToken);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<ApiResponse>(CancellationToken);
        return data!;
    }
}
```

Common places to pass the token:

```csharp
// HTTP calls
await httpClient.GetAsync(url, CancellationToken);

// EF Core queries
await context.Users.FirstOrDefaultAsync(u => u.Id == id, CancellationToken);
await context.SaveChangesAsync(CancellationToken);

// Task.Delay (useful for polling or retry logic)
await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);

// Channel operations
await channel.Writer.WriteAsync(item, CancellationToken);

// Stream reads
await stream.ReadAsync(buffer, CancellationToken);
```

You can also check for cancellation manually:

```csharp
public override async Task<BatchResult> Run(BatchRequest input)
{
    var results = new List<ItemResult>();

    foreach (var item in input.Items)
    {
        // Check before each expensive iteration
        CancellationToken.ThrowIfCancellationRequested();

        results.Add(await ProcessItem(item));
    }

    return new BatchResult(results);
}
```

## Cancellation Behavior

### Pre-Cancelled Token

If the token is already cancelled when a junction is about to execute, `OperationCanceledException` is thrown **before** `Run()` is called. The junction never executes:

```csharp
using var cts = new CancellationTokenSource();
cts.Cancel();

// Throws OperationCanceledException, no junctions execute
await train.Run(input, cts.Token);
```

### Cancellation Between Junctions

If a token is cancelled after Junction 1 completes but before Junction 2 starts, Junction 2 is skipped:

```
Junction 1 executes ✓
                    ← token cancelled here
Junction 2 skipped (ThrowIfCancellationRequested fires)
Junction 3 skipped
```

### Cancellation During a Junction

If a junction is awaiting an async operation when the token is cancelled, the operation throws `OperationCanceledException` which propagates up:

```csharp
// This junction will be interrupted if the token is cancelled during the delay
public override async Task<string> Run(string input)
{
    await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken);
    return input;
}
```

### Cancellation vs. Exceptions

Cancellation is treated differently from regular exceptions:

- **Regular exceptions** are enriched with `TrainExceptionData` (junction name, train name, original stack trace, etc.) via `Exception.Data` and returned as `Left` in the Railway pattern. The original exception type and message are preserved - callers outside Trax see the exception exactly as the junction threw it
- **`OperationCanceledException` while the train's token is cancelled** is passed on without enrichment. It is not a junction failure, it is an explicit abort signal
- **`OperationCanceledException` while the train's token is not cancelled**, such as an `HttpClient` timeout, is treated as a regular exception: enriched and returned as `Left`

`Run` rethrows it, so a caller of `Run` sees cancellation as an exception, the .NET convention. `RunEither` lets nothing escape, so it returns the `OperationCanceledException` as `Left`: check `is OperationCanceledException` before treating a `Left` as a business failure. `ServiceTrain` relies on this to record the run as `Cancelled`.

### TrainState.Cancelled

When an `OperationCanceledException` that the run was asked for ends it, `FinishServiceTrain` sets the train state to `Cancelled` instead of `Failed`. A run is asked to stop when its own token is cancelled, or when its persisted cancel flag is set (the dashboard's cancel button, or the scheduler's job timeout for a run on another host), which `CancellationCheckProvider` turns into a cancellation at the next junction boundary:

```
OperationCanceledException, requested     → TrainState.Cancelled
OperationCanceledException, not requested → TrainState.Failed, FailureClass Transient
All other exceptions                      → TrainState.Failed
No exception                              → TrainState.Completed
```

Cancelled trains are **not retried** and **do not create dead letters**. Cancellation is a deliberate operator action, not a transient failure. A cancelled run of a scheduled manifest consumes the occurrence it ran for: the manifest next runs at its next scheduled occurrence, not on the next polling cycle. An `OperationCanceledException` nothing asked for is the opposite case: most often an `HttpClient` timeout, it means a dependency was slow, so the run is recorded as a failure, classified `Transient` unless your `IFailureClassifier` answers otherwise, and a manifest retries it. `OnFailed` fires for it, not `OnCancelled`. The dashboard shows cancelled trains with a warning (orange) badge to distinguish them from failures.

## TrainBus Dispatch

When using `ITrainBus` for dynamic train dispatch, pass the token as the second argument:

```csharp
public class OrderService(ITrainBus trainBus)
{
    public async Task<OrderResult> ProcessOrder(
        OrderInput input,
        CancellationToken cancellationToken)
    {
        return await trainBus.RunAsync<OrderResult>(input, cancellationToken);
    }
}
```

The full set of `RunAsync` overloads:

```csharp
// Without cancellation (existing API, unchanged)
Task<TOut> RunAsync<TOut>(object input, Metadata? metadata = null);
Task RunAsync(object input, Metadata? metadata = null);

// With cancellation
Task<TOut> RunAsync<TOut>(object input, CancellationToken ct, Metadata? metadata = null);
Task RunAsync(object input, CancellationToken ct, Metadata? metadata = null);
```

## Background Services and Shutdown

All Trax.Core background services propagate their `stoppingToken` to train executions. This means when your application shuts down (e.g., `Ctrl+C`, SIGTERM, or `IHostApplicationLifetime.StopApplication()`), in-flight trains receive a cancellation signal.

### Polling Services

The ManifestManager, JobDispatcher, and MetadataCleanup polling services all pass `stoppingToken` to `train.Run()`:

```csharp
// Inside ManifestManagerPollingService (simplified)
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await RunManifestManager(stoppingToken);
        await Task.Delay(pollingInterval, stoppingToken);
    }
}

private async Task RunManifestManager(CancellationToken cancellationToken)
{
    await train.Run(Unit.Default, cancellationToken);
}
```

### LocalWorkerService Shutdown Grace Period

The LocalWorkerService implements a **shutdown grace period** using an unlinked CancellationTokenSource. When the host signals shutdown, in-flight trains get `ShutdownTimeout` (default: 30 seconds) to finish before being cancelled:

```
Host signals shutdown (stoppingToken fires)
        │
        ▼
┌──────────────────────────────────────────┐
│  shutdownCts.CancelAfter(ShutdownTimeout) │  ← 30 second grace period starts
│                                            │
│  In-flight train continues running...   │
│  ... has 30 seconds to complete ...        │
│                                            │
│  After 30s: shutdownCts fires             │  ← train receives cancellation
└──────────────────────────────────────────┘
```

This gives trains performing critical operations (database transactions, external API calls) time to complete cleanly rather than being aborted mid-operation.

Once a train finishes, whether inside the grace period or after being cancelled, the worker deletes its `background_job` row without the stopping token. That token has already fired by then, and the delete is bookkeeping for finished work, so it is not cancellable. If it were, every job that finished during shutdown would leave its row behind for another worker to re-claim after `VisibilityTimeout`, only to refuse it as no longer `Pending`.

With a `BatchSize` above 1, a job the worker claimed but had not started when shutdown began is not started at all: its `fetched_at` is cleared so another worker can claim it straight away, rather than every remaining job getting its own grace period.

Configure the grace period:

```csharp
.ConfigureLocalWorkers(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60); // default: 30 seconds
})
```

*See also: [Job Submission](/docs/scheduler/job-submission)*

## ServiceTrain Token Propagation

`ServiceTrain` (the database-tracked train base class) propagates the token to all its internal operations:

- `SaveChangesAsync(CancellationToken)`: transaction commits use the token
- `BeginTransaction(CancellationToken)`: transaction starts use the token
- Junction effect providers receive the token for their before/after hooks

With one deliberate exception: **the write that records how the train ended does not use the caller's token.**

If a train is cancelled mid-execution, `ServiceTrain.Run` captures the `OperationCanceledException` as the run's result instead of letting it escape. It then calls `FinishServiceTrain`, which sets `Cancelled` and `EndTime` on the metadata and clears the junction progress columns (`CurrentlyRunningJunction` and `JunctionStartedAt`), and saves that. Only after the outcome is saved do the `OnCancelled` hooks run and the exception get rethrown to the caller, so you get an audit trail even for cancelled trains. If saving the outcome itself throws, that error is logged and the `OperationCanceledException` still propagates.

That audit trail only exists because the terminal `SaveChanges` runs on `CancellationToken.None` rather than the caller's token. The caller's token is cancelled in precisely the case the record is written for, so using it would mean the row could never be updated: the execution would stay `InProgress` with no `EndTime`, and a scheduler's `ReapStaleInProgressMetadataJunction` would later rewrite it to `Failed` after `StaleInProgressTimeout`. That is the wrong terminal state, and for a train whose work completed despite the cancellation it is a false one.

The same applies on the success path. A train whose downstream call takes no token finishes its work even after the caller has disconnected; that run is recorded as `Completed`, because what happened is what gets recorded, not what the caller was still waiting for.

## Cancelling Running Trains

Trax.Core supports two complementary cancellation paths: **same-server** (instant) and **cross-server** (between-junction).

### Same-Server: ICancellationRegistry

When the scheduler is configured, `LocalWorkerService` registers each in-flight train's `CancellationTokenSource` with `ICancellationRegistry`. Calling `TryCancel(metadataId)` fires the CTS immediately, interrupting the train mid-junction. The first registration for a run holds it until that worker removes it with `Unregister(metadataId, cts)`, so a second delivery of the same job on the same host (which does not run the train) cannot take the registration from the job that is running:

```
Dashboard "Cancel" button
    ├──→ SET cancel_requested = true in DB  (always)
    └──→ ICancellationRegistry.TryCancel()  (same-server bonus)
            ├─ Found → CTS.Cancel() → in-flight async op throws OCE instantly
            └─ Not found → no-op (cross-server handled by DB flag)
```

### Cross-Server: CancellationCheckProvider

For multi-server deployments where the cancelling server may not be the one executing the train, the `CancellationCheckProvider` junction effect queries the `cancel_requested` column before each junction:

```
CancellationCheckProvider.BeforeJunctionExecution()
    → SELECT cancel_requested FROM metadata WHERE id = @id
    → if true: throw OperationCanceledException
    → train terminates at next junction boundary
```

Enable both paths with a single call:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJunctionProgress()  // Adds CancellationCheckProvider + JunctionProgressProvider
    )
);
```

*See also: [Junction Progress](/docs/effect/effect-providers/junction-progress)*

## Programmatic Cancellation API

`ITraxScheduler` provides methods for cancelling running jobs from user code:

```csharp
// Cancel all running executions of a specific manifest
int cancelled = await scheduler.CancelAsync("my-job-external-id");

// Cancel all running executions in a manifest group
int cancelled = await scheduler.CancelGroupAsync(groupId);
```

Both methods use dual-layer cancellation:
1. **Database flag** (`CancellationRequested = true`): works cross-server, picked up by `CancellationCheckProvider` at the next junction boundary
2. **Same-server instant cancel** (`ICancellationRegistry.TryCancel()`): immediately fires the `CancellationTokenSource` if the job is running on the same server

Cancelled trains transition to `TrainState.Cancelled`, are **not retried**, and **do not create dead letters**. A cancelled run of a scheduled manifest consumes the occurrence it ran for: the manifest next runs at its next scheduled occurrence, not on the next polling cycle.

## Automatic Timeout Cancellation

The ManifestManager automatically cancels jobs that exceed their configured timeout. Each polling cycle, the `CancelTimedOutJobsJunction` checks every InProgress run and cancels any where the elapsed time exceeds its manifest's `TimeoutSeconds`, or the global `DefaultJobTimeout` when the manifest sets none or the run has no manifest. Runs of a manifest disabled while they run are still timed out.

This is distinct from dead-lettering. Timeout cancellation actively interrupts the running train rather than waiting for it to fail and then moving it to the dead letter queue. The job transitions to `TrainState.Cancelled` and is not retried: an hourly job that times out runs again at its next hourly occurrence, so a job that always exceeds its timeout runs once an hour rather than continuously.

Configure timeouts per-manifest or globally:

```csharp
// Per-manifest timeout
await scheduler.ScheduleAsync<IMyTrain, MyInput, Unit>(
    "my-job", new MyInput(), Every.Minutes(5),
    options => options.Timeout(TimeSpan.FromMinutes(10)));

// Global default timeout
.AddScheduler(scheduler => scheduler
    .DefaultJobTimeout(TimeSpan.FromMinutes(30)))
```

## IJobSubmitter

Custom job submitter implementations can accept a `CancellationToken` via default interface methods:

```csharp
public interface IJobSubmitter
{
    Task<string> EnqueueAsync(long metadataId);
    Task<string> EnqueueAsync(long metadataId, object input);

    // Default implementations for cancellation support
    Task<string> EnqueueAsync(long metadataId, CancellationToken ct) =>
        EnqueueAsync(metadataId);
    Task<string> EnqueueAsync(long metadataId, object input, CancellationToken ct) =>
        EnqueueAsync(metadataId, input);
}
```

The built-in submitters implement the CT overloads. `PostgresJobSubmitter` passes the token to `SaveChangesAsync`, and the in-memory submitter that `UseInMemory()` selects passes it to `train.Run()`.

## Testing with Cancellation Tokens

### Verify a junction respects the token

```csharp
[Test]
public async Task Junction_Cancellation_StopsExecution()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var junction = new CountingJunction();
    var train = new TestTrain(junction);

    // Cancelled token prevents the junction from executing
    var act = () => train.Run("input", cts.Token);
    await act.Should().ThrowAsync<Exception>();

    junction.ExecutionCount.Should().Be(0);
}
```

### Verify a junction uses the token for async operations

```csharp
[Test]
public async Task Junction_UsesToken_ForAsyncCalls()
{
    using var cts = new CancellationTokenSource();
    var junction = new TokenCapturingJunction();
    var train = new TestTrain(junction);

    await train.Run("input", cts.Token);

    junction.CapturedToken.Should().Be(cts.Token);
}

private class TokenCapturingJunction : Junction<string, string>
{
    public CancellationToken CapturedToken { get; private set; }

    public override Task<string> Run(string input)
    {
        CapturedToken = CancellationToken;
        return Task.FromResult(input);
    }
}
```

### Verify mid-execution cancellation

```csharp
[Test]
public async Task Train_CancelDuringJunction_PropagatesCancellation()
{
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(50));

    var train = new SlowTrain();

    var act = () => train.Run("input", cts.Token);
    await act.Should().ThrowAsync<Exception>();
}
```

*See also: [Testing](/docs/cross-cutting/testing)*

## Summary

| Layer | How the token arrives | What it's used for |
|-------|----------------------|-------------------|
| **Train** | `Run(input, ct)` or `RunEither(input, ct)` | Stored on `Train.CancellationToken` property |
| **Junction** | Copied from train before `Run()` is called | Access via `this.CancellationToken` in `Run()` |
| **TrainBus** | `RunAsync<TOut>(input, ct)` | Forwarded to `train.Run(input, ct)` |
| **ServiceTrain** | Inherited from `Train` | Passed to `SaveChangesAsync`, `BeginTransaction` |
| **Background Services** | `stoppingToken` from `ExecuteAsync` | Passed to `train.Run(input, stoppingToken)` |
| **LocalWorkerService** | `shutdownCts.Token` (grace period) | Passed to `train.Run(input, shutdownCts.Token)` |
| **Job Submitter** | `EnqueueAsync(id, ct)` | Passed to `SaveChangesAsync` / `train.Run()` |
| **Dashboard** | Component disposal token | Passed to event handler async calls |
| **CancellationCheckProvider** | DB `cancel_requested` flag | Throws `OperationCanceledException` before junction |
| **ICancellationRegistry** | `CancellationTokenSource` lookup | `TryCancel()` fires CTS for same-server instant cancel |

## SDK Reference

> [Run / RunEither](/docs/sdk-reference/train-methods/run) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | [CancelAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers)
