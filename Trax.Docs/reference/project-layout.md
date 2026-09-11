---
layout: default
title: Project Layout
parent: Reference
nav_order: 11
---

# Project Layout

Where each kind of thing lives. Before adding one, find an existing example of the same kind
and mirror it: the same directory depth, the same naming, the same partial-class split.

## Data models

A model is two files in two projects.

```
Trax.Effect/src/Trax.Effect/Models/<Entity>/<Entity>.cs
Trax.Effect/src/Trax.Effect.Data/Models/<Entity>/Persistent<Entity>.cs
```

The base class carries no EF dependency, which is what lets `Trax.Effect` be referenced
without EF Core. The mapping carries the `[Table]` and `[Column]` attributes. Each provider
has its own `DbContext` subclass at
`Trax.Effect.Data.<Provider>/Services/<Provider>Context/<Provider>Context.cs`, sharing shape
through the base `DataContext<TDbContext>`.

## Migrations

`Trax.Effect.Data.<Provider>/Migrations/<NNN>_<name>.sql`, numbered sequentially from 001
with no gaps, embedded by a csproj glob. See [Writing Migrations](/docs/reference/writing-migrations).

## Builders

A partial class in its own directory, one file per feature area. See
[Builder Pattern](/docs/reference/builder-pattern).

## Sample applications

A sample is at least three projects:

```
Trax.Samples.<Name>            the train library, with [TraxQuery] trains under Trains/
Trax.Samples.<Name>.Api        the GraphQL host (or .Hub)
Trax.Samples.<Name>.Client     the consumer (or .Scheduler, .Worker)
```

Resolvers and inline `[ExtendObjectType]` classes belong in trains in the library project,
not in `Program.cs`.

## Tests

See [Test Conventions](/docs/reference/test-conventions) for the folder layout and the
fixture patterns.

## When you cannot find a pattern

Ask before improvising. A new pattern introduced without justification is almost always a
missed pattern that already exists.
