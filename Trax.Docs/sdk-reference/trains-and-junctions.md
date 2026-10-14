---
title: Trains and Junctions
description: "Index of the train and junction base classes, the contracts they implement, the exception a failure carries and the helpers that register them."
type: landing
area: trains
parent: SDK Reference
has_children: true
nav_order: 1
---

# Trains and Junctions

The types a train library derives from and registers. The methods called inside `Junctions()` are under [Train Methods](/docs/sdk-reference/train-methods); how a train runs is in [Trains and Junctions](/docs/core/trains-and-junctions).

## Trax.Core

| Page | Description |
|------|-------------|
| [Train](/docs/sdk-reference/trains-and-junctions/train) | `Train<TInput, TReturn>`, the base class, with `Run`, `RunEither`, `ExternalId` and the chain methods |
| [Junction](/docs/sdk-reference/trains-and-junctions/junction) | `Junction<TIn, TOut>` and `IJunction<TIn, TOut>`, one unit of work on the railway |
| [IRoute](/docs/sdk-reference/trains-and-junctions/i-route) | `IRoute<TIn, TOut>`, the one-method contract every train implements |
| [MonadTask](/docs/sdk-reference/trains-and-junctions/monad-task) | `MonadTask<TInput, TReturn>` and `Monad<TInput, TReturn>`, what a chain step returns |
| [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | `TrainException`, `TrainExceptionData` and `FailureClass`, how a failure is raised and recorded |
| [Either and Unit](/docs/sdk-reference/trains-and-junctions/either) | `Either<TLeft, TRight>`, `EitherStatus` and `Unit`, the result type `Junctions()` returns |
| [FunctionalExtensions](/docs/sdk-reference/trains-and-junctions/functional-extensions) | `AssertLoaded` and `AssertEachLoaded`, guards against unloaded EF navigations |

## Trax.Effect

| Page | Description |
|------|-------------|
| [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train) | `ServiceTrain<TIn, TOut>` and `IServiceTrain<TIn, TOut>`: metadata, effects, lifecycle hooks, container-built junctions |
| [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) | `EffectJunction<TIn, TOut>`, a junction the junction effects observe |
| [Route and Junction Registration](/docs/sdk-reference/trains-and-junctions/route-registration) | `AddScopedTraxRoute`, `AddTransientTraxRoute`, `AddSingletonTraxRoute` and the `*TraxJunction` aliases |
