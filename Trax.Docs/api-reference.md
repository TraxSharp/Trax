---
layout: default
title: API Reference
nav_order: 13
has_children: true
---

# API Reference

Complete reference documentation for every user-facing method in Trax.Core. Each page documents the method signature, all parameters, return type, and usage examples.

For conceptual explanations and tutorials, see [Core Concepts]({{ site.baseurl }}{% link concepts.md %}) and [Usage Guide]({{ site.baseurl }}{% link usage-guide.md %}).

## Categories

### [Train Methods]({{ site.baseurl }}{% link api-reference/train-methods.md %})

Methods available inside `RunInternal` on `Train<TInput, TReturn>` — the core building blocks for composing steps into a pipeline.

Includes: `Activate`, `Chain`, `ShortCircuit`, `Extract`, `AddServices`, `Resolve`, `Run` / `RunEither`.

### [Configuration]({{ site.baseurl }}{% link api-reference/configuration.md %})

The `AddTrax.CoreEffects` entry point and every extension method on `Trax.CoreEffectConfigurationBuilder` — data providers, effect providers, and orchestration setup.

Includes: `AddPostgresEffect`, `AddInMemoryEffect`, `AddJsonEffect`, `SaveTrainParameters`, `AddStepLogger`, `AddServiceTrainBus`, `AddEffect`, `AddStepEffect`, `SetEffectLogLevel`.

### [Scheduler API]({{ site.baseurl }}{% link api-reference/scheduler-api.md %})

Scheduler configuration (`AddScheduler` + `SchedulerConfigurationBuilder`) and the runtime `IManifestScheduler` interface for scheduling, managing, and monitoring recurring trains.

Includes: `AddScheduler`, `UseHangfire`, `Schedule`, `ScheduleMany`, dependent scheduling, manifest management, scheduling helpers (`Every`, `Cron`, `ManifestOptions`).

### [Mediator API]({{ site.baseurl }}{% link api-reference/mediator-api.md %})

The `ITrainBus` interface for dynamically dispatching trains by input type.

Includes: `RunAsync`, `InitializeTrain`, `AddServiceTrainBus`.

### [Dashboard API]({{ site.baseurl }}{% link api-reference/dashboard-api.md %})

Setup and configuration for the Trax.Core Blazor dashboard.

Includes: `AddTrax.CoreDashboard`, `UseTrax.CoreDashboard`, `DashboardOptions`.

### [DI Registration]({{ site.baseurl }}{% link api-reference/di-registration.md %})

Helper methods for registering trains and steps with `[Inject]` property injection support.

Includes: `AddScopedTrax.CoreRoute`, `AddTransientTrax.CoreRoute`, `AddSingletonTrax.CoreRoute`, and step equivalents.
