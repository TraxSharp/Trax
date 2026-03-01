---
layout: default
title: Cancellation Tokens
parent: Usage Guide
nav_order: 12
---

# Cancellation Tokens

Trax.Core threads `CancellationToken` through the entire pipeline — from the initial `Run` call, through every step, down to EF Core queries and background service shutdown. This enables graceful cancellation of workflows in response to HTTP request aborts, application shutdown, or explicit user cancellation.

## How It Works

CancellationToken propagation is **property-based**, not parameter-based. The token is stored as a property on `Train` and `Step`, so the `Step.Run(TIn input)` signature stays unchanged:

```
Run(input, cancellationToken)
        │
        ▼
┌──────────────────────────┐
│      Train                │
│  CancellationToken = ct  │
└──────────┬───────────────┘
           │  Chain(step)
           ▼
┌──────────────────────────┐
│      Step                 │
│  CancellationToken = ct  │  ← set automatically before Run() is called
│  Run(input)               │  ← your code accesses this.CancellationToken
└──────────────────────────┘
```

1. The caller passes a `CancellationToken` to `workflow.Run(input, cancellationToken)`
2. The workflow stores it on its `CancellationToken` property
3. Before each step executes, `RailwayStep` copies the token from the workflow to the step
4. The step checks `CancellationToken.ThrowIfCancellationRequested()` before `Run()` is called
5. Inside `Run()`, your code accesses `this.CancellationToken` for async operations

## Passing a Token to a Workflow

Every `Run` and `RunEither` overload has a `CancellationToken` variant:

```csharp
// Throws on failure
await workflow.Run(input, cancellationToken);
await workflow.Run(input, serviceProvider, cancellationToken);

// Returns Either<Exception, TReturn>
await workflow.RunEither(input, cancellationToken);
await workflow.RunEither(input, serviceProvider, cancellationToken);
```

If you call `Run(input)` without a token, `CancellationToken` defaults to `CancellationToken.None` — all existing code works unchanged.

*API Reference: [Run / RunEither]({{ site.baseurl }}{% link api-reference/workflow-methods/run.md %})*

## Using the Token Inside Steps

Access `this.CancellationToken` in your step's `Run` method. It is set automatically before `Run` is called — you never need to set it yourself:

```csharp
public class FetchDataStep(IHttpClientFactory httpFactory) : Step<FetchRequest, ApiResponse>
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

If the token is already cancelled when a step is about to execute, `OperationCanceledException` is thrown **before** `Run()` is called. The step never executes:

```csharp
using var cts = new CancellationTokenSource();
cts.Cancel();

// Throws OperationCanceledException — no steps execute
await workflow.Run(input, cts.Token);
```

### Cancellation Between Steps

If a token is cancelled after Step 1 completes but before Step 2 starts, Step 2 is skipped:

```
Step 1 executes ✓
                    ← token cancelled here
Step 2 skipped (ThrowIfCancellationRequested fires)
Step 3 skipped
```

### Cancellation During a Step

If a step is awaiting an async operation when the token is cancelled, the operation throws `OperationCanceledException` which propagates up:

```csharp
// This step will be interrupted if the token is cancelled during the delay
public override async Task<string> Run(string input)
{
    await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken);
    return input;
}
```

### Cancellation vs. Exceptions

Cancellation is treated differently from regular exceptions:

- **Regular exceptions** are wrapped with `WorkflowExceptionData` (step name, workflow name, etc.) and returned as `Left` in the Railway pattern
- **`OperationCanceledException`** propagates cleanly without wrapping — it is not a step failure, it is an explicit abort signal

This means cancellation always throws (even with `RunEither`), which matches the .NET convention that cancellation is exceptional flow, not a business error.

### WorkflowState.Cancelled

When an `OperationCanceledException` reaches `FinishWorkflow`, the workflow state is set to `Cancelled` instead of `Failed`:

```
OperationCanceledException → WorkflowState.Cancelled
All other exceptions       → WorkflowState.Failed
No exception               → WorkflowState.Completed
```

Cancelled workflows are **not retried** and **do not create dead letters**. Cancellation is a deliberate operator action, not a transient failure. The dashboard shows cancelled workflows with a warning (orange) badge to distinguish them from failures.

## WorkflowBus Dispatch

When using `IWorkflowBus` for dynamic workflow dispatch, pass the token as the second argument:

```csharp
public class OrderService(IWorkflowBus workflowBus)
{
    public async Task<OrderResult> ProcessOrder(
        OrderInput input,
        CancellationToken cancellationToken)
    {
        return await workflowBus.RunAsync<OrderResult>(input, cancellationToken);
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

*API Reference: [WorkflowBus]({{ site.baseurl }}{% link api-reference/mediator-api/workflow-bus.md %})*

## Background Services and Shutdown

All Trax.Core background services propagate their `stoppingToken` to workflow executions. This means when your application shuts down (e.g., `Ctrl+C`, SIGTERM, or `IHostApplicationLifetime.StopApplication()`), in-flight workflows receive a cancellation signal.

### Polling Services

The ManifestManager, JobDispatcher, and MetadataCleanup polling services all pass `stoppingToken` to `workflow.Run()`:

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
    await workflow.Run(Unit.Default, cancellationToken);
}
```

### PostgresWorkerService Shutdown Grace Period

The PostgresWorkerService implements a **shutdown grace period** using an unlinked CancellationTokenSource. When the host signals shutdown, in-flight workflows get `ShutdownTimeout` (default: 30 seconds) to finish before being cancelled:

```
Host signals shutdown (stoppingToken fires)
        │
        ▼
┌──────────────────────────────────────────┐
│  shutdownCts.CancelAfter(ShutdownTimeout) │  ← 30 second grace period starts
│                                            │
│  In-flight workflow continues running...   │
│  ... has 30 seconds to complete ...        │
│                                            │
│  After 30s: shutdownCts fires             │  ← workflow receives cancellation
└──────────────────────────────────────────┘
```

This ensures workflows performing critical operations (database transactions, external API calls) have time to complete cleanly rather than being aborted mid-operation.

Configure the grace period:

```csharp
.UsePostgresTaskServer(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60); // default: 30 seconds
})
```

*See also: [Task Server]({{ site.baseurl }}{% link scheduler/task-server.md %})*

## ServiceTrain Token Propagation

`ServiceTrain` (the database-tracked workflow base class) propagates the token to all its internal operations:

- `SaveChangesAsync(CancellationToken)` — transaction commits use the token
- `BeginTransaction(CancellationToken)` — transaction starts use the token
- Step effect providers receive the token for their before/after hooks

If a workflow is cancelled mid-execution, the `ServiceTrain` catch block still runs `FinishWorkflow` to record the cancellation in Metadata — so you get an audit trail even for cancelled workflows. `FinishWorkflow` also clears the step progress columns (`CurrentlyRunningStep` and `StepStartedAt`) as a safety net.

## Cancelling Running Workflows

Trax.Core supports two complementary cancellation paths: **same-server** (instant) and **cross-server** (between-step).

### Same-Server: ICancellationRegistry

When the scheduler is configured, `PostgresWorkerService` registers each in-flight workflow's `CancellationTokenSource` with `ICancellationRegistry`. Calling `TryCancel(metadataId)` fires the CTS immediately, interrupting the workflow mid-step:

```
Dashboard "Cancel" button
    ├──→ SET cancel_requested = true in DB  (always)
    └──→ ICancellationRegistry.TryCancel()  (same-server bonus)
            ├─ Found → CTS.Cancel() → in-flight async op throws OCE instantly
            └─ Not found → no-op (cross-server handled by DB flag)
```

### Cross-Server: CancellationCheckProvider

For multi-server deployments where the cancelling server may not be the one executing the workflow, the `CancellationCheckProvider` step effect queries the `cancel_requested` column before each step:

```
CancellationCheckProvider.BeforeStepExecution()
    → SELECT cancel_requested FROM metadata WHERE id = @id
    → if true: throw OperationCanceledException
    → workflow terminates at next step boundary
```

Enable both paths with a single call:

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddStepProgress()  // Adds CancellationCheckProvider + StepProgressProvider
);
```

*See also: [Step Progress]({{ site.baseurl }}{% link usage-guide/effect-providers/step-progress.md %})*

## IBackgroundTaskServer

Custom task server implementations can accept a `CancellationToken` via default interface methods:

```csharp
public interface IBackgroundTaskServer
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

The built-in `PostgresTaskServer` and `InMemoryTaskServer` both implement the CT overloads — `PostgresTaskServer` passes the token to `SaveChangesAsync`, and `InMemoryTaskServer` passes it to `workflow.Run()`.

## Testing with Cancellation Tokens

### Verify a step respects the token

```csharp
[Test]
public async Task Step_Cancellation_StopsExecution()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var step = new CountingStep();
    var workflow = new TestWorkflow(step);

    // Cancelled token prevents the step from executing
    var act = () => workflow.Run("input", cts.Token);
    await act.Should().ThrowAsync<Exception>();

    step.ExecutionCount.Should().Be(0);
}
```

### Verify a step uses the token for async operations

```csharp
[Test]
public async Task Step_UsesToken_ForAsyncCalls()
{
    using var cts = new CancellationTokenSource();
    var step = new TokenCapturingStep();
    var workflow = new TestWorkflow(step);

    await workflow.Run("input", cts.Token);

    step.CapturedToken.Should().Be(cts.Token);
}

private class TokenCapturingStep : Step<string, string>
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
public async Task Workflow_CancelDuringStep_PropagatesCancellation()
{
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(50));

    var workflow = new SlowWorkflow();

    var act = () => workflow.Run("input", cts.Token);
    await act.Should().ThrowAsync<Exception>();
}
```

*See also: [Testing]({{ site.baseurl }}{% link usage-guide/testing.md %})*

## Summary

| Layer | How the token arrives | What it's used for |
|-------|----------------------|-------------------|
| **Train** | `Run(input, ct)` or `RunEither(input, ct)` | Stored on `Train.CancellationToken` property |
| **Step** | Copied from workflow before `Run()` is called | Access via `this.CancellationToken` in `Run()` |
| **WorkflowBus** | `RunAsync<TOut>(input, ct)` | Forwarded to `workflow.Run(input, ct)` |
| **ServiceTrain** | Inherited from `Train` | Passed to `SaveChangesAsync`, `BeginTransaction` |
| **Background Services** | `stoppingToken` from `ExecuteAsync` | Passed to `workflow.Run(input, stoppingToken)` |
| **PostgresWorkerService** | `shutdownCts.Token` (grace period) | Passed to `workflow.Run(input, shutdownCts.Token)` |
| **Task Server** | `EnqueueAsync(id, ct)` | Passed to `SaveChangesAsync` / `workflow.Run()` |
| **Dashboard** | Component disposal token | Passed to event handler async calls |
| **CancellationCheckProvider** | DB `cancel_requested` flag | Throws `OperationCanceledException` before step |
| **ICancellationRegistry** | `CancellationTokenSource` lookup | `TryCancel()` fires CTS for same-server instant cancel |
