---
layout: default
title: AddLifecycleHook
parent: Configuration
grand_parent: SDK Reference
nav_order: 11
---

# AddLifecycleHook

Registers a lifecycle hook that fires on train state transitions. Use lifecycle hooks to trigger side effects when trains start, complete, fail, or are cancelled, without coupling your train logic to the side effect.

## Signatures

Register a hook directly (recommended):

```csharp
public static TraxEffectBuilder AddLifecycleHook<T>(
    this TraxEffectBuilder builder,
    bool toggleable = true
) where T : class // ITrainLifecycleHook or ITrainLifecycleHookFactory
```

When `T` implements `ITrainLifecycleHook`, a factory is created internally and there is no need to write a factory class. When `T` implements `ITrainLifecycleHookFactory`, it is registered directly (advanced).

Register with an existing factory instance:

```csharp
public static TraxEffectBuilder AddLifecycleHook<TFactory>(
    this TraxEffectBuilder builder,
    TFactory factory,
    bool toggleable = true
) where TFactory : class, ITrainLifecycleHookFactory
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `builder` | `TraxEffectBuilder` | Yes | N/A | The effect configuration builder |
| `factory` | `TFactory` | No | N/A | An existing factory instance (when not using DI to create it) |
| `toggleable` | `bool` | No | `true` | Whether the hook can be enabled/disabled at runtime via `IEffectRegistry` |

## ITrainLifecycleHook

```csharp
public interface ITrainLifecycleHook
{
    Task OnStarted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
    Task OnCompleted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
    Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) => Task.CompletedTask;
    Task OnCancelled(Metadata metadata, CancellationToken ct) => Task.CompletedTask;
}
```

All methods have default implementations that return `Task.CompletedTask`. Override only the events you care about.

| Method | When it fires |
|--------|---------------|
| `OnStarted` | After the train's metadata is persisted and before the train's junctions execute |
| `OnCompleted` | After a successful run, after output is persisted |
| `OnFailed` | After a failed run (exception that is not `OperationCanceledException`), after failure is persisted |
| `OnCancelled` | After cancellation (`OperationCanceledException`), after cancellation is persisted |

### Accessing Train Input and Output

#### Availability by hook

The input is set before junctions execute; the output is set only after a successful run. This determines which hooks can access each value:

| Hook | `TrainInput` / `GetInput<T>()` | `TrainOutput` / `GetOutput<T>()` |
|------|------|-------|
| `OnStarted` | Yes | No (train hasn't run yet) |
| `OnCompleted` | Yes | Yes |
| `OnFailed` | Yes | No (train failed before producing output) |
| `OnCancelled` | Yes | No (train was cancelled) |

When unavailable, both return `default` (`null` for reference types, zero for value types).

#### Typed access via `Metadata`

`Metadata` provides generic methods to retrieve the input and output as their original types:

```csharp
T? GetInput<T>()
T? GetOutput<T>()
```

These return `default` if the object is null or not of the requested type. Useful in global lifecycle hooks (`ITrainLifecycleHook`) where you know the train's types:

```csharp
public class SlackNotificationHook(ISlackClient slack) : ITrainLifecycleHook
{
    public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        var input = metadata.GetInput<BanPlayerInput>();
        await slack.PostAsync($"Ban failed for player {input?.PlayerId}: {exception.Message}", ct);
    }
}
```

#### Typed access via `TrainInput` / `TrainOutput`

Per-train lifecycle hooks have an even simpler option. `ServiceTrain<TIn, TOut>` exposes typed properties that are automatically resolved from the metadata:

```csharp
protected TIn TrainInput   // available in all hooks
protected TOut TrainOutput  // available in OnCompleted (default in OnFailed/OnCancelled)
```

No casting, no type parameters. The types come from the train's generic arguments:

```csharp
public class BanPlayerTrain(ILogger<BanPlayerTrain> logger)
    : ServiceTrain<BanPlayerInput, Unit>, IBanPlayerTrain
{
    protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        logger.LogInformation("Banned player {PlayerId} for: {Reason}",
            TrainInput?.PlayerId, TrainInput?.Reason);
        return Task.CompletedTask;
    }
}
```

#### Serialized JSON output

`metadata.Output` is always available as serialized JSON in `OnCompleted` hooks, regardless of whether [`SaveTrainParameters()`](/docs/sdk-reference/configuration/save-train-parameters) is configured. When `SaveTrainParameters()` is not configured, the output is serialized in-memory before hooks fire but is not persisted to the database. This means GraphQL subscriptions and custom hooks can always read `metadata.Output` without requiring `SaveTrainParameters()`.

## ITrainLifecycleHookFactory (Advanced)

```csharp
public interface ITrainLifecycleHookFactory
{
    ITrainLifecycleHook Create();
}
```

Most users do not need to implement this interface. `AddLifecycleHook<THook>()` generates a factory automatically. Use a custom factory only if you need non-standard creation logic. The factory is a singleton; it creates a new hook instance per train execution.

## Error Handling

Lifecycle hook exceptions are **caught and logged, never propagated**. A failing hook will never cause a train to fail. This is intentional: side effects like posting to Grafana or sending Slack notifications should not affect train reliability.

## Example: Custom Slack Notification Hook

```csharp
public class SlackNotificationHook(ISlackClient slack) : ITrainLifecycleHook
{
    public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        await slack.PostAsync($"Train {metadata.Name} failed: {exception.Message}", ct);
}
```

Registration:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddLifecycleHook<SlackNotificationHook>()
    )
    .AddMediator(ServiceLifetime.Scoped, typeof(Program).Assembly)
);
```

No factory class needed. Trax creates one internally and resolves your hook's constructor dependencies from DI.

## Built-in Hooks

| Hook | Package | Description |
|------|---------|-------------|
| `GraphQLSubscriptionHook` | `Trax.Api.GraphQL` | Publishes lifecycle events to GraphQL [subscriptions](/docs/sdk-reference/graphql-api/subscriptions). Automatically registered by `AddTraxGraphQL()`. |
| `BroadcastLifecycleHook` | `Trax.Effect` | Publishes lifecycle events to a message bus for cross-process delivery. Automatically registered by [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster). |

## Per-Train Lifecycle Hooks

In addition to global hooks registered via `AddLifecycleHook<T>()`, individual trains can override lifecycle methods directly on `ServiceTrain`. This is useful when a hook only makes sense for one specific train and you want to use the train's own injected dependencies.

```csharp
public class BanPlayerTrain(ILogger<BanPlayerTrain> logger)
    : ServiceTrain<BanPlayerInput, Unit>, IBanPlayerTrain
{
    protected override Unit Junctions() => Chain<ApplyBanJunction>();

    protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        logger.LogInformation("Banned player {PlayerId} for: {Reason}",
            TrainInput?.PlayerId, TrainInput?.Reason);
        return Task.CompletedTask;
    }

    protected override Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        logger.LogWarning("Failed to ban player {PlayerId}: {Message}",
            TrainInput?.PlayerId, exception.Message);
        return Task.CompletedTask;
    }
}
```

No registration needed, just `override` the method. The available methods match the global hook interface:

| Method | Signature |
|--------|-----------|
| `OnStarted` | `protected virtual Task OnStarted(Metadata metadata, CancellationToken ct)` |
| `OnCompleted` | `protected virtual Task OnCompleted(Metadata metadata, CancellationToken ct)` |
| `OnFailed` | `protected virtual Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)` |
| `OnCancelled` | `protected virtual Task OnCancelled(Metadata metadata, CancellationToken ct)` |

All default to no-op (`Task.CompletedTask`). Override only the ones you need.

In addition to the `metadata` parameter, per-train hooks can use the `TrainInput` and `TrainOutput` properties for typed access to the train's input and output without casting. See [Accessing Train Input and Output](#accessing-train-input-and-output) above.

### Global vs Per-Train Hooks

| | Global (`AddLifecycleHook<T>`) | Per-Train (`override`) |
|---|---|---|
| Scope | Fires for every train | Fires only for the overriding train |
| Registration | `AddLifecycleHook<T>()` in builder | No registration, just override |
| DI access | Constructor injection on the hook class | Constructor injection on the train itself |
| Execution order | Fires first | Fires after global hooks |
| Error handling | Caught and logged | Caught and logged |
| Use case | Cross-cutting concerns (metrics, audit logs) | Train-specific business logic (alerts, cleanup) |

## Package

```
dotnet add package Trax.Effect
```
