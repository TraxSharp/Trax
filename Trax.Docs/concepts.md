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
│                       Train                          │
│         Orchestrates steps, manages effects             │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                        Steps                            │
│  [Validate] ────► [Create] ────► [Notify]              │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                       Effects                           │
│     Database    │    JSON Log    │    Parameters        │
└─────────────────────────────────────────────────────────┘
```

A **train** is a sequence of steps that accomplish a business operation. `CreateUserTrain` chains together validation, database insertion, and email notification.

A **step** does one thing. `ValidateEmailStep` checks if an email is valid. `CreateUserInDatabaseStep` inserts a record. Steps are easy to test in isolation because they have a single responsibility.

**Effects** are side effects that happen as a result of steps running—database writes, log entries, serialized parameters. Effect providers track these during train execution and save them atomically at the end.
