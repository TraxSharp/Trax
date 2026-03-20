---
layout: default
title: Cancellation Tokens
parent: Cross-Cutting
nav_order: 1
---

# Cancellation Tokens

Trax.Core threads `CancellationToken` through the entire pipeline — from the initial `Run` call, through every junction, down to EF Core queries and background service shutdown. This enables graceful cancellation of trains in response to HTTP request aborts, application shutdown, or explicit user cancellation.

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

Every `Run` and `RunEither` overload has a `CancellationToken` variant:

```csharp
// Throws on failure
await train.Run(input, cancellationToken);
await train.Run(input, serviceProvider, cancellationToken);

// Returns Either<Exception, TReturn>
await train.RunEither(input, cancellationToken);
await train.RunEither(input, serviceProvider, cancellationToken);
```

If you call `Run(input)` without a token, `CancellationToken` defaults to `CancellationToken.None` — all existing code works unchanged.

## Using the Token Inside Junctions

Access `this.CancellationToken` in your junction's `Run` method. It is set automatically before `Run` is called — you never need to set it yourself:

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

// Throws OperationCanceledException — no junctions execute
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

- **Regular exceptions** are wrapped with `TrainExceptionData` (junction name, train name, etc.) and returned as `Left` in the Railway pattern
- **`OperationCanceledException`** propagates cleanly without wrapping — it is not a junction failure, it is an explicit abort signal

This means cancellation always throws (even with `RunEither`), which matches the .NET convention that cancellation is exceptional flow, not a business error.

### TrainState.Cancelled

When an `OperationCanceledException` reaches `FinishTrain`, the train state is set to `Cancelled` instead of `Failed`:

```
OperationCanceledException → TrainState.Cancelled
All other exceptions       → TrainState.Failed
No exception               → TrainState.Completed
```

Cancelled trains are **not retried** and **do not create dead letters**. Cancellation is a deliberate operator action, not a transient failure. The dashboard shows cancelled trains with a warning (orange) badge to distinguish them from failures.

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

This ensures trains performing critical operations (database transactions, external API calls) have time to complete cleanly rather than being aborted mid-operation.

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

- `SaveChangesAsync(CancellationToken)` — transaction commits use the token
- `BeginTransaction(CancellationToken)` — transaction starts use the token
- Junction effect providers receive the token for their before/after hooks

If a train is cancelled mid-execution, the `ServiceTrain` catch block still runs `FinishTrain` to record the cancellation in Metadata — so you get an audit trail even for cancelled trains. `FinishTrain` also clears the junction progress columns (`CurrentlyRunningJunction` and `JunctionStartedAt`) as a safety net.

## Cancelling Running Trains

Trax.Core supports two complementary cancellation paths: **same-server** (instant) and **cross-server** (between-junction).

### Same-Server: ICancellationRegistry

When the scheduler is configured, `LocalWorkerService` registers each in-flight train's `CancellationTokenSource` with `ICancellationRegistry`. Calling `TryCancel(metadataId)` fires the CTS immediately, interrupting the train mid-junction:

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
1. **Database flag** (`CancellationRequested = true`) — works cross-server, picked up by `CancellationCheckProvider` at the next junction boundary
2. **Same-server instant cancel** (`ICancellationRegistry.TryCancel()`) — immediately fires the `CancellationTokenSource` if the job is running on the same server

Cancelled trains transition to `TrainState.Cancelled`, are **not retried**, and **do not create dead letters**.

## Automatic Timeout Cancellation

The ManifestManager automatically cancels jobs that exceed their configured timeout. Each polling cycle, the `CancelTimedOutJobsJunction` checks all InProgress metadata and cancels any where the elapsed time exceeds the manifest's `TimeoutSeconds` (or the global `DefaultJobTimeout`).

This is distinct from dead-lettering — timeout cancellation actively interrupts the running train rather than waiting for it to fail and then moving it to the dead letter queue. The job transitions to `TrainState.Cancelled` and is not retried.

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

The built-in `PostgresJobSubmitter` and `InMemoryJobSubmitter` both implement the CT overloads — `PostgresJobSubmitter` passes the token to `SaveChangesAsync`, and `InMemoryJobSubmitter` passes it to `train.Run()`.

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
