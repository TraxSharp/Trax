---
layout: default
title: AddEffect / AddStepEffect
parent: Configuration
grand_parent: API Reference
nav_order: 8
---

# AddEffect / AddStepEffect

Registers custom effect provider factories. `AddEffect` registers workflow-level effects (run once per workflow execution); `AddStepEffect` registers step-level effects (run once per step).

## AddEffect Signatures

```csharp
// Type only, auto-created via DI
public static Trax.CoreEffectConfigurationBuilder AddEffect<TEffectProviderFactory>(
    this Trax.CoreEffectConfigurationBuilder builder,
    bool toggleable = true
) where TEffectProviderFactory : class, IEffectProviderFactory

// Pre-created instance
public static Trax.CoreEffectConfigurationBuilder AddEffect<TEffectProviderFactory>(
    this Trax.CoreEffectConfigurationBuilder builder,
    TEffectProviderFactory factory,
    bool toggleable = true
) where TEffectProviderFactory : class, IEffectProviderFactory

// Interface + implementation, auto-created
public static Trax.CoreEffectConfigurationBuilder AddEffect<TIEffectProviderFactory, TEffectProviderFactory>(
    this Trax.CoreEffectConfigurationBuilder builder,
    bool toggleable = true
) where TIEffectProviderFactory : class, IEffectProviderFactory
  where TEffectProviderFactory : class, TIEffectProviderFactory

// Interface + implementation, pre-created instance
public static Trax.CoreEffectConfigurationBuilder AddEffect<TIEffectProviderFactory, TEffectProviderFactory>(
    this Trax.CoreEffectConfigurationBuilder builder,
    TEffectProviderFactory factory,
    bool toggleable = true
) where TIEffectProviderFactory : class, IEffectProviderFactory
  where TEffectProviderFactory : class, TIEffectProviderFactory
```

## AddStepEffect Signatures

```csharp
// Type only, auto-created
public static Trax.CoreEffectConfigurationBuilder AddStepEffect<TStepEffectProviderFactory>(
    this Trax.CoreEffectConfigurationBuilder builder,
    bool toggleable = true
) where TStepEffectProviderFactory : class, IStepEffectProviderFactory

// Pre-created instance
public static Trax.CoreEffectConfigurationBuilder AddStepEffect<TStepEffectProviderFactory>(
    this Trax.CoreEffectConfigurationBuilder builder,
    TStepEffectProviderFactory factory,
    bool toggleable = true
) where TStepEffectProviderFactory : class, IStepEffectProviderFactory

// Interface + implementation, pre-created instance
public static Trax.CoreEffectConfigurationBuilder AddStepEffect<TIStepEffectProviderFactory, TStepEffectProviderFactory>(
    this Trax.CoreEffectConfigurationBuilder builder,
    TStepEffectProviderFactory factory,
    bool toggleable = true
) where TIStepEffectProviderFactory : class, IStepEffectProviderFactory
  where TStepEffectProviderFactory : class, TIStepEffectProviderFactory
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `toggleable` | `bool` | No | `true` | Whether the effect can be enabled/disabled at runtime via the effect registry. Set to `false` for essential effects (like data persistence). |
| `factory` | `TEffectProviderFactory` | No | — | A pre-created factory instance (instance overloads only) |

## Returns

`Trax.CoreEffectConfigurationBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax.CoreEffects(options => options
    .AddEffect<MyCustomEffectProviderFactory>()
    .AddStepEffect<MyCustomStepEffectProviderFactory>(toggleable: false)
);
```

## Configurable Factories

Factories can optionally expose runtime-configurable settings by implementing `IConfigurableEffectProviderFactory<TConfiguration>` (or `IConfigurableStepEffectProviderFactory<TConfiguration>` for step effects). The configuration type is a POCO class registered as a singleton in DI.

```csharp
public class MyEffectConfiguration
{
    public bool EnableDetailedLogging { get; set; } = false;
}

public class MyEffectProviderFactory(MyEffectConfiguration config)
    : IConfigurableEffectProviderFactory<MyEffectConfiguration>
{
    public MyEffectConfiguration Configuration => config;

    public IEffectProvider Create() => new MyEffectProvider(config);
}
```

Register the configuration alongside the factory:

```csharp
services.AddTrax.CoreEffects(options =>
{
    var config = new MyEffectConfiguration { EnableDetailedLogging = true };
    options.ServiceCollection.AddSingleton(config);
    options.AddEffect<MyEffectProviderFactory>();
});
```

Configurable factories appear with a settings button on the dashboard's [Effects page]({{ site.baseurl }}{% link dashboard.md %}#effects-page), where their configuration properties can be modified at runtime.

## Remarks

- **Workflow-level effects** (`AddEffect`): Implement `IEffectProviderFactory`. Called at workflow start and end. Used for cross-cutting concerns like data persistence, audit logging, etc.
- **Step-level effects** (`AddStepEffect`): Implement `IStepEffectProviderFactory`. Called before and after each step. Used for step-level logging, metrics, etc.
- The interface+implementation overloads register the factory under both the interface type and `IEffectProviderFactory`, enabling resolution by either type.
- See [Effect Providers]({{ site.baseurl }}{% link usage-guide/effect-providers.md %}) for conceptual background on the effect system.
