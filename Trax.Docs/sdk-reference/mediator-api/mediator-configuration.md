---
layout: default
title: MediatorConfiguration
description: Reference for MediatorConfiguration, the read-only singleton holding the settings AddMediator resolved from its builder.
parent: Mediator API
grand_parent: SDK Reference
nav_order: 10
---

# MediatorConfiguration

The settings `AddMediator` resolved from its builder, registered as a singleton. Inject it to read what the host was configured with, for example in a test that asserts a limit. Every setter is internal: the builder is the only way to change a value.

## Signature

```csharp
namespace Trax.Mediator.Configuration;

public class MediatorConfiguration
{
    public ServiceLifetime TrainLifetime { get; }
    public Assembly[] Assemblies { get; }
    public int? GlobalMaxConcurrentRun { get; }
    public int? PerPrincipalMaxConcurrentRun { get; }
    public int MaxInputJsonBytes { get; }
    public TimeSpan MaxQueueHookDuration { get; }
    public bool AllowMissingAuthorizationService { get; }
    public bool SkipChainVerification { get; }
}
```

## Properties

| Property | Default | Set with | Description |
|----------|---------|----------|-------------|
| `TrainLifetime` | `Transient` | `TrainLifetime(ServiceLifetime)` | The lifetime discovered trains are registered with. `Singleton` is refused. |
| `Assemblies` | empty | `ScanAssemblies(...)` | The assemblies scanned for `IServiceTrain<,>` implementations, including any an earlier subsystem contributed (such as `AddStateMachines`) |
| `GlobalMaxConcurrentRun` | `null` (no limit) | `GlobalConcurrentRunLimit(int)` | Concurrent direct runs across every train |
| `PerPrincipalMaxConcurrentRun` | `null` (no limit) | `PerPrincipalMaxConcurrentRun(int)` | Concurrent direct runs per principal. Has no effect until an [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider) that returns ids is registered. |
| `MaxInputJsonBytes` | `262144` (256 KiB) | `WithMaxInputJsonBytes(int)` | The UTF-8 size cap on caller-supplied input JSON for `RunAsync` and `QueueAsync`, checked after authorization and before deserialization. A queued entry's stored, re-serialized input has its own, larger cap derived from this one. |
| `MaxQueueHookDuration` | 30 seconds | `WithMaxQueueHookDuration(TimeSpan)` | How long an `OnQueue` hook may run while its enqueue holds a connection and a transaction. Past it the enqueue fails with `QueueHookTimeoutException` and rolls back. `Timeout.InfiniteTimeSpan` removes the limit. |
| `AllowMissingAuthorizationService` | `false` | `AllowMissingAuthorizationService()` | Lets a host with `[TraxAuthorize]` trains start without an `ITrainAuthorizationService`, for scheduler-only or dashboard-only processes |
| `SkipChainVerification` | `false` | `SkipChainVerification()` | Skips the startup check that reads every registered train's chain |

Per-train limits from `ConcurrentRunLimit<TTrain>(int)` are kept internally and are not exposed here.

## Example

```csharp
public class MediatorSettingsTests
{
    [Test]
    public void Host_caps_input_at_64_KiB()
    {
        using var provider = BuildHostServices();

        var configuration = provider.GetRequiredService<MediatorConfiguration>();

        configuration.MaxInputJsonBytes.Should().Be(65_536);
    }
}
```

See [AddMediator](/docs/sdk-reference/configuration/add-mediator) for the builder.

## Package

```
dotnet add package Trax.Mediator
```
