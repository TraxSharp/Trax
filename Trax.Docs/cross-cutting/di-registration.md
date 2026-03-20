---
layout: default
title: DI Registration
parent: Cross-Cutting
nav_order: 6
has_children: true
---

# DI Registration

Helper extension methods for registering Trax trains and junctions with the .NET dependency injection container. These methods wrap the standard `AddScoped`/`AddTransient`/`AddSingleton` registrations and add support for `[Inject]` property injection — a pattern used by `ServiceTrain` to inject services like `IEffectRunner` and `ILogger`.

Use these instead of raw DI registration when your train or junction class uses `[Inject]` properties.

{: .sdk-references }
> [Train Registration](/docs/cross-cutting/di-registration/train-registration) | [Junction Registration](/docs/cross-cutting/di-registration/junction-registration)

```csharp
services.AddTransientTraxRoute<IMyTrain, MyTrain>();
services.AddScopedTraxJunction<IMyJunction, MyJunction>();
```

| Page | Description |
|------|-------------|
| [Train Registration](/docs/cross-cutting/di-registration/train-registration) | `AddScoped/Transient/SingletonTraxRoute` methods |
| [Junction Registration](/docs/cross-cutting/di-registration/junction-registration) | `AddScoped/Transient/SingletonTraxJunction` methods |
