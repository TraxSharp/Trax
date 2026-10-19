---
title: AddEffect / AddJunctionEffect
description: Reference for AddEffect and AddJunctionEffect, which register train-level and junction-level effect provider factories, including configurable ones.
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 30
package: Trax.Effect
namespace:
  - Trax.Effect.Extensions
  - Trax.Effect.Services.EffectProviderFactory
  - Trax.Effect.Services.JunctionEffectProviderFactory
  - Trax.Effect.Services.EffectRegistry
source:
  - Trax.Effect/src/Trax.Effect/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect/Services/EffectProviderFactory
  - Trax.Effect/src/Trax.Effect/Services/JunctionEffectProviderFactory
---

# AddEffect / AddJunctionEffect

Register effect provider factories. `AddEffect` registers a train-level effect, whose provider tracks a run's models
and saves when the run's rows are written. `AddJunctionEffect` registers a junction-level effect, whose provider runs
before and after every `EffectJunction`; see [IJunctionEffectProvider](/docs/sdk-reference/configuration/i-junction-effect-provider).

## Signatures

```csharp fragment
public static TraxEffectBuilder AddEffect<TEffectProviderFactory>(
    this TraxEffectBuilder builder,
    bool toggleable = true
)
    where TEffectProviderFactory : class, IEffectProviderFactory

public static TraxEffectBuilder AddEffect<TEffectProviderFactory>(
    this TraxEffectBuilder builder,
    TEffectProviderFactory factory,
    bool toggleable = true
)
    where TEffectProviderFactory : class, IEffectProviderFactory

public static TraxEffectBuilder AddEffect<TIEffectProviderFactory, TEffectProviderFactory>(
    this TraxEffectBuilder builder,
    bool toggleable = true
)
    where TIEffectProviderFactory : class, IEffectProviderFactory
    where TEffectProviderFactory : class, TIEffectProviderFactory

public static TraxEffectBuilder AddEffect<TIEffectProviderFactory, TEffectProviderFactory>(
    this TraxEffectBuilder builder,
    TEffectProviderFactory factory,
    bool toggleable = true
)
    where TIEffectProviderFactory : class, IEffectProviderFactory
    where TEffectProviderFactory : class, TIEffectProviderFactory
```

```csharp fragment
public static TraxEffectBuilder AddJunctionEffect<TJunctionEffectProviderFactory>(
    this TraxEffectBuilder builder,
    bool toggleable = true
)
    where TJunctionEffectProviderFactory : class, IJunctionEffectProviderFactory

public static TraxEffectBuilder AddJunctionEffect<TJunctionEffectProviderFactory>(
    this TraxEffectBuilder builder,
    TJunctionEffectProviderFactory factory,
    bool toggleable = true
)
    where TJunctionEffectProviderFactory : class, IJunctionEffectProviderFactory

public static TraxEffectBuilder AddJunctionEffect<TIJunctionEffectProviderFactory, TJunctionEffectProviderFactory>(
    this TraxEffectBuilder builder,
    TJunctionEffectProviderFactory factory,
    bool toggleable = true
)
    where TIJunctionEffectProviderFactory : class, IJunctionEffectProviderFactory
    where TJunctionEffectProviderFactory : class, TIJunctionEffectProviderFactory
```

```csharp fragment
public interface IEffectProviderFactory
{
    IEffectProvider Create();
}

public interface IJunctionEffectProviderFactory
{
    IJunctionEffectProvider Create();
}

public interface IConfigurableProviderFactory
{
    object GetConfiguration();
    Type GetConfigurationType();
}

public interface IConfigurableEffectProviderFactory<TConfiguration>
    : IEffectProviderFactory, IConfigurableProviderFactory
    where TConfiguration : class
{
    TConfiguration Configuration { get; }
}

public interface IConfigurableJunctionEffectProviderFactory<TConfiguration>
    : IJunctionEffectProviderFactory, IConfigurableProviderFactory
    where TConfiguration : class
{
    TConfiguration Configuration { get; }
}
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `factory` | the factory type | | An instance to register, on the overloads that take one; the others let the container build the factory |
| `toggleable` | `bool` | `true` | Whether the effect registry, and so the dashboard's Effects page, may turn the effect off at runtime. `false` for an effect a run cannot do without, as the data providers register theirs. |

## Returns

`TraxEffectBuilder`, for chaining. These do not keep `TraxEffectBuilderWithData`; call them before the data
provider when a later call needs it.

## Behavior

Each overload registers the factory as a singleton under `IEffectProviderFactory` (or `IJunctionEffectProviderFactory`)
and records its type in the `IEffectRegistry` with `toggleable`.

| Overload | Registered under |
|----------|------------------|
| Type only | The factory type, and the provider factory interface |
| Instance | The provider factory interface only |
| Interface and implementation | The implementation, the interface you name, and the provider factory interface, all the same instance |

`IEffectRegistry` tracks each registered type and whether it is enabled. `IsToggleable(type)` and
`IsToggleable<TFactory>()` say whether it may be switched; `Enable` and `Disable` switch it, and runners created
afterwards leave a disabled factory out. `GetToggleable()` lists the switchable ones.

A factory that implements `IConfigurableEffectProviderFactory<TConfiguration>` (or
`IConfigurableJunctionEffectProviderFactory<TConfiguration>`) exposes a configuration object. The default
implementations of `GetConfiguration()` and `GetConfigurationType()` return `Configuration` and `typeof(TConfiguration)`.
The operations console's [Effects page](/docs/dashboard/system#effects) shows a settings button for it and edits that object's
properties in place, so the factory and its providers read the configuration rather than copy it.

## Examples

```csharp compile
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddEffect<AuditEffectFactory>()
        .AddJunctionEffect<TimingEffectFactory>(toggleable: false)
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);

public sealed class AuditEffectFactory : IEffectProviderFactory
{
    public IEffectProvider Create() => throw new NotImplementedException();
}

public sealed class TimingEffectFactory : IJunctionEffectProviderFactory
{
    public IJunctionEffectProvider Create() => throw new NotImplementedException();
}
```

A configurable factory, with its configuration registered beside it:

```csharp compile
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;

services.AddTrax(trax => trax
    .AddEffects(effects =>
    {
        effects.ServiceCollection.AddSingleton(new AuditConfiguration { Detailed = true });
        return effects.AddEffect<ConfigurableAuditFactory>().UsePostgres(connectionString);
    })
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);

public class AuditConfiguration
{
    public bool Detailed { get; set; }
}

public sealed class ConfigurableAuditFactory(AuditConfiguration configuration)
    : IConfigurableEffectProviderFactory<AuditConfiguration>
{
    public AuditConfiguration Configuration => configuration;

    public IEffectProvider Create() => throw new NotImplementedException();
}
```

## See also

- [Effect Providers](/docs/effect/effect-providers)
- [IJunctionEffectProvider](/docs/sdk-reference/configuration/i-junction-effect-provider), [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
