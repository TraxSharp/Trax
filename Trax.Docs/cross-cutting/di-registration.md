---
layout: default
title: DI Registration
parent: Cross-Cutting
nav_order: 6
has_children: true
---

# DI Registration

Helper extension methods for registering Trax trains and steps with the .NET dependency injection container. These methods wrap the standard `AddScoped`/`AddTransient`/`AddSingleton` registrations and add support for `[Inject]` property injection — a pattern used by `ServiceTrain` to inject services like `IEffectRunner` and `ILogger`.

Use these instead of raw DI registration when your train or step class uses `[Inject]` properties.

```csharp
services.AddTransientTraxRoute<IMyTrain, MyTrain>();
services.AddScopedTraxStep<IMyStep, MyStep>();
```

| Page | Description |
|------|-------------|
| [Train Registration]({{ site.baseurl }}{% link cross-cutting/di-registration/train-registration.md %}) | `AddScoped/Transient/SingletonTraxRoute` methods |
| [Step Registration]({{ site.baseurl }}{% link cross-cutting/di-registration/step-registration.md %}) | `AddScoped/Transient/SingletonTraxStep` methods |
