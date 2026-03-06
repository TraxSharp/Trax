---
layout: default
title: AddEffect / AddStepEffect
parent: Configuration
grand_parent: SDK Reference
nav_order: 8
---

# AddEffect / AddStepEffect

Registers custom effect provider factories. `AddEffect` registers train-level effects (run once per train execution); `AddStepEffect` registers step-level effects (run once per step).

## AddEffect Signatures

```csharp
// Type only, auto-created via DI
public static TraxEffectBuilder AddEffect<TEffectProviderFactory>(
    this TraxEffectBuilder builder,
    bool toggleable = true
) where TEffectProviderFactory : class, IEffectProviderFactory

// Pre-created instance
public static TraxEffectBuilder AddEffect<TEffectProviderFactory>(
    this TraxEffectBuilder builder,
    TEffectProviderFactory factory,
    bool toggleable = true
) where TEffectProviderFactory : class, IEffectProviderFactory

// Interface + implementation, auto-created
public static TraxEffectBuilder AddEffect<TIEffectProviderFactory, TEffectProviderFactory>(
    this TraxEffectBuilder builder,
    bool toggleable = true
) where TIEffectProviderFactory : class, IEffectProviderFactory
  where TEffectProviderFactory : class, TIEffectProviderFactory

// Interface + implementation, pre-created instance
public static TraxEffectBuilder AddEffect<TIEffectProviderFactory, TEffectProviderFactory>(
    this TraxEffectBuilder builder,
    TEffectProviderFactory factory,
    bool toggleable = true
) where TIEffectProviderFactory : class, IEffectProviderFactory
  where TEffectProviderFactory : class, TIEffectProviderFactory
```

## AddStepEffect Signatures

```csharp
// Type only, auto-created
public static TraxEffectBuilder AddStepEffect<TStepEffectProviderFactory>(
    this TraxEffectBuilder builder,
    bool toggleable = true
) where TStepEffectProviderFactory : class, IStepEffectProviderFactory

// Pre-created instance
public static TraxEffectBuilder AddStepEffect<TStepEffectProviderFactory>(
    this TraxEffectBuilder builder,
    TStepEffectProviderFactory factory,
    bool toggleable = true
) where TStepEffectProviderFactory : class, IStepEffectProviderFactory

// Interface + implementation, pre-created instance
public static TraxEffectBuilder AddStepEffect<TIStepEffectProviderFactory, TStepEffectProviderFactory>(
    this TraxEffectBuilder builder,
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

`TraxEffectBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddEffect<MyCustomEffectProviderFactory>()
        .AddStepEffect<MyCustomStepEffectProviderFactory>(toggleable: false)
    )
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
services.AddTrax(trax => trax
    .AddEffects(effects =>
    {
        var config = new MyEffectConfiguration { EnableDetailedLogging = true };
        effects.ServiceCollection.AddSingleton(config);
        effects.AddEffect<MyEffectProviderFactory>();
    })
);
```

Configurable factories appear with a settings button on the dashboard's [Effects page]({{ site.baseurl }}{% link dashboard.md %}#effects-page), where their configuration properties can be modified at runtime.

## Remarks

- **Train-level effects** (`AddEffect`): Implement `IEffectProviderFactory`. Called at train start and end. Used for cross-cutting concerns like data persistence, audit logging, etc.
- **Step-level effects** (`AddStepEffect`): Implement `IStepEffectProviderFactory`. Called before and after each step. Used for step-level logging, metrics, etc.
- The interface+implementation overloads register the factory under both the interface type and `IEffectProviderFactory`, enabling resolution by either type.
- See [Effect Providers]({{ site.baseurl }}{% link effect/effect-providers.md %}) for conceptual background on the effect system.
