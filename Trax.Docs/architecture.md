---
layout: default
title: Architecture
nav_order: 5
has_children: true
---

# Architecture

How Trax.Core's components fit together.

## System Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Client Layer                                │
│    [CLI Applications]  [Web Applications]  [API Controllers]        │
└─────────────────────────────────┬───────────────────────────────────┘
                                  │
          ┌───────────────────────┼───────────────────────┐
          ▼                       ▼                       ▼
┌──────────────────┐  ┌────────────────────┐  ┌──────────────────────┐
│ Dashboard (Opt.) │  │     Mediator       │  │  Scheduler (Opt.)    │
│ [Blazor Server]  │  │  [TrainBus]     │  │  [ManifestManager]   │
└────────┬─────────┘  └─────────┬──────────┘  └──────────┬───────────┘
         │                      │                        │
         │                      │◄───────────────────────┘
         │                      │  (Scheduler uses TrainBus)
         └──────────┬───────────┘
                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     Trax.Effect                               │
│    [ServiceTrain] ────► [EffectRunner] ────► [EffectProviders]     │
└─────────────────────────────────┬───────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Core Trax.Core                                │
│           [Train Engine] ────────► [Steps]                       │
└─────────────────────────────────┬───────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Effect Implementations                           │
│  [Data Provider]  [JSON Provider]  [Parameter Provider]  [Custom]   │
└────────────┬──────────────────────────────────────────┬─────────────┘
             │                                          │
             ▼                                          ▼
┌─────────────────────────┐                ┌─────────────────────────┐
│       PostgreSQL        │                │        InMemory         │
└─────────────────────────┘                └─────────────────────────┘
```

## Package Hierarchy

```
Trax.Core (Core)
    │
    ├─── Trax.Core.Analyzers (Compile-Time Chain Validation)
    │
    └─── Trax.Effect (Enhanced Trains)
              │
              ├─── Trax.Dashboard (Web UI)
              │
              ├─── Trax.Mediator (TrainBus)
              │
              ├─── Trax.Effect.Data (Abstract Persistence)
              │         │
              │         ├─── Trax.Effect.Data.Postgres
              │         └─── Trax.Effect.Data.InMemory
              │
              ├─── Trax.Scheduler (Job Orchestration)
              │         │
              │         └─── Trax.Scheduler.Hangfire
              │
              ├─── Trax.Effect.Provider.Json
              ├─── Trax.Effect.Provider.Parameter
              └─── Trax.Effect.StepProvider.Logging
```

## Repository Structure

```
src/
├── core/           Trax.Core
├── effect/         Trax.Effect
├── analyzers/      Trax.Core.Analyzers (Roslyn compile-time validation)
├── data/           Trax.Effect.Data
│                   Trax.Effect.Data.InMemory
│                   Trax.Effect.Data.Postgres
├── providers/      Trax.Effect.Provider.Json
│                   Trax.Effect.Provider.Parameter
│                   Trax.Effect.StepProvider.Logging
├── orchestration/  Trax.Mediator
│                   Trax.Scheduler
│                   Trax.Scheduler.Hangfire
└── dashboard/      Trax.Dashboard
plugins/
├── vscode/         Trax.Core Chain Hints (VSCode inlay hints extension)
└── rider-resharper/ Trax.Core Chain Hints (Rider/ReSharper inlay hints plugin)
tests/              Test projects
samples/            Sample applications
docs/               Documentation (GitHub Pages)
```
