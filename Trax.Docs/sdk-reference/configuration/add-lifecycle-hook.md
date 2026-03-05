---
layout: default
title: AddLifecycleHook
parent: Configuration
grand_parent: SDK Reference
nav_order: 11
---

# AddLifecycleHook

Registers a lifecycle hook that fires on train state transitions. Use lifecycle hooks to trigger side effects when trains start, complete, fail, or are cancelled — without coupling your train logic to the side effect.

## Signatures

```csharp
public static TraxEffectConfigurationBuilder AddLifecycleHook<TFactory>(
    this TraxEffectConfigurationBuilder builder,
    bool toggleable = true
) where TFactory : class, ITrainLifecycleHookFactory
```

```csharp
public static TraxEffectConfigurationBuilder AddLifecycleHook<TFactory>(
    this TraxEffectConfigurationBuilder builder,
    TFactory factory,
    bool toggleable = true
) where TFactory : class, ITrainLifecycleHookFactory
```

```csharp
public static TraxEffectConfigurationBuilder AddLifecycleHook<TIFactory, TFactory>(
    this TraxEffectConfigurationBuilder builder,
    TFactory factory,
    bool toggleable = true
) where TIFactory : class, ITrainLifecycleHookFactory
  where TFactory : class, TIFactory
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `builder` | `TraxEffectConfigurationBuilder` | Yes | — | The effect configuration builder |
| `factory` | `TFactory` | No | — | An existing factory instance (when not using DI to create it) |
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
| `OnStarted` | After the train's metadata is persisted and before `RunInternal` executes |
| `OnCompleted` | After a successful run, after output is persisted |
| `OnFailed` | After a failed run (exception that is not `OperationCanceledException`), after failure is persisted |
| `OnCancelled` | After cancellation (`OperationCanceledException`), after cancellation is persisted |

## ITrainLifecycleHookFactory

```csharp
public interface ITrainLifecycleHookFactory
{
    ITrainLifecycleHook Create();
}
```

Mirrors the existing `IEffectProviderFactory` / `IStepEffectProviderFactory` pattern. The factory is a singleton; it creates a new hook instance per train execution.

## Error Handling

Lifecycle hook exceptions are **caught and logged, never propagated**. A failing hook will never cause a train to fail. This is intentional — side effects like posting to Grafana or sending Slack notifications should not affect train reliability.

## Example: Custom Slack Notification Hook

```csharp
public class SlackNotificationHook(ISlackClient slack) : ITrainLifecycleHook
{
    public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        await slack.PostAsync($"Train {metadata.Name} failed: {exception.Message}", ct);
}

public class SlackNotificationHookFactory(IServiceProvider sp) : ITrainLifecycleHookFactory
{
    public ITrainLifecycleHook Create() =>
        sp.GetRequiredService<SlackNotificationHook>();
}
```

Registration:

```csharp
builder.Services.AddTraxEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddServiceTrainBus(ServiceLifetime.Scoped, typeof(Program).Assembly)
    .AddLifecycleHook<SlackNotificationHookFactory>()
);
```

## Built-in Hooks

| Hook | Package | Description |
|------|---------|-------------|
| `GraphQLSubscriptionHook` | `Trax.Api.GraphQL` | Publishes lifecycle events to GraphQL [subscriptions]({{ site.baseurl }}{% link sdk-reference/graphql-api/subscriptions.md %}). Automatically registered by `AddTraxGraphQL()`. |

## Package

```
dotnet add package Trax.Effect
```
