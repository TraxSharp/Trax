---
layout: default
title: Core Concepts
nav_order: 3
has_children: true
---

# Core Concepts

Trax.Core borrows concepts from functional programming and applies them to train orchestration in .NET.

## Trains, Steps, and Effects

```
┌─────────────────────────────────────────────────────────┐
│                       Train                             │
│       Carries cargo through a route of stops            │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                   Stops (Steps)                         │
│  [Validate] ────► [Create] ────► [Notify]              │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                  Station Services (Effects)              │
│     Database    │    JSON Log    │    Parameters        │
└─────────────────────────────────────────────────────────┘
```

A **train** travels a route of stops to accomplish a business operation. `CreateUserTrain` carries a request through validation, database insertion, and email notification. The data it carries between stops is its **cargo** ([Memory](concepts/memory.md)).

A **step** is a stop on the route. Each stop does one thing: `ValidateEmailStep` inspects the cargo, `CreateUserInDatabaseStep` produces a new record. Steps are easy to test in isolation because each one has a single responsibility.

**Effects** are station services — operations that happen as the train passes through each stop. Database persistence, log entries, serialized parameters. Effect providers handle these behind the scenes and save them atomically when the journey completes.
