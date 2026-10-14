---
title: Configuration
description: "Index of the configuration reference: AddTrax, AddEffects and AddMediator, the data providers, effects, deciders, broadcasters and data services, by package."
type: landing
area: effects
parent: SDK Reference
has_children: true
nav_order: 4
---

# Configuration

Every Trax host is configured through [AddTrax](/docs/sdk-reference/configuration/add-trax), whose builder takes
[AddEffects](/docs/sdk-reference/configuration/add-effects), then [AddMediator](/docs/sdk-reference/configuration/add-mediator),
then [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler). The pages below are grouped by the package
each member ships in.

## Trax.Effect

- [AddTrax](/docs/sdk-reference/configuration/add-trax): the entry point, `TraxBuilder` and what it registers
- [AddEffects](/docs/sdk-reference/configuration/add-effects): `TraxEffectBuilder`, `TraxEffectBuilderWithData` and the effect configuration
- [Host Tracking](/docs/sdk-reference/configuration/host-tracking): the host identity methods and `TraxHostInfo`
- [AddEffect / AddJunctionEffect](/docs/sdk-reference/configuration/add-effect): custom effect provider factories and the effect registry
- [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook): `ITrainLifecycleHook` and its factory
- [SetEffectLogLevel](/docs/sdk-reference/configuration/set-effect-log-level): the level the logging effects write at
- [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier): how a failed run's exception becomes a `FailureClass`
- [IJunctionEffectProvider](/docs/sdk-reference/configuration/i-junction-effect-provider): code run before and after every `EffectJunction`
- [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster): carries lifecycle events between processes

## Trax.Mediator

- [AddMediator](/docs/sdk-reference/configuration/add-mediator): train discovery, `TraxMediatorBuilder` and the startup chain check

## Trax.Effect.Data

- [AddDataContextLogging](/docs/sdk-reference/configuration/add-data-context-logging): stores `ILogger` lines in `trax.log`
- [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording): records decisions and replays them into repeated runs
- [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events): publishes and records each step of a run
- [IDataContext](/docs/sdk-reference/configuration/i-data-context): Trax's EF Core context over the `trax` tables
- [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context): the base class and helpers for your own contexts
- [ISqlDialect](/docs/sdk-reference/configuration/i-sql-dialect): the SQL that differs between providers
- [IQueuedWorkListener](/docs/sdk-reference/configuration/i-queued-work-listener): the notice that wakes the job dispatcher
- [LikePattern](/docs/sdk-reference/configuration/like-pattern): escaped, lowered `LIKE` patterns for text search

## Data providers

- [UsePostgres](/docs/sdk-reference/configuration/use-postgres) (`Trax.Effect.Data.Postgres`): the PostgreSQL provider
- [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) (`Trax.Effect.Data.Postgres`): turns off the startup migration
- [DatabaseMigrator](/docs/sdk-reference/configuration/database-migrator) (`Trax.Effect.Data.Postgres`): applies the Postgres schema
- [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) (`Trax.Effect.Data.Sqlite`): the SQLite provider
- [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) (`Trax.Effect.Data.InMemory`): the in-memory provider

## Effect providers

- [AddJson](/docs/sdk-reference/configuration/add-json) (`Trax.Effect.Provider.Json`): logs tracked models when they change
- [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) (`Trax.Effect.Provider.Parameter`): stores inputs and outputs
- [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) (`Trax.Effect.JunctionProvider.Logging`): logs each junction
- [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) (`Trax.Effect.JunctionProvider.Progress`): records the running junction and checks for cancellation

## Trax.Effect.Decisions.SystemOne

- [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider): answers decisions with a Nimble server
- [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider): answers decisions through a System One model

## Broadcasters

- [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) (`Trax.Effect.Broadcaster.RabbitMQ`): the RabbitMQ transport
- [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub) (`Trax.Effect.Broadcaster.SignalR`): the SignalR sink
- [MapTraxTrainEventHub](/docs/sdk-reference/configuration/map-trax-train-event-hub) (`Trax.Effect.Broadcaster.SignalR`): maps the hub endpoint
- [ITraxTrainEventClient](/docs/sdk-reference/configuration/i-trax-train-event-client) (`Trax.Effect.Broadcaster.SignalR`): the methods clients receive
