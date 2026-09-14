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

A persisted model is a pair of files in two projects:

```
Trax.Effect/src/Trax.Effect/Models/<Entity>/<Entity>.cs
Trax.Effect/src/Trax.Effect.Data/Models/<Entity>/Persistent<Entity>.cs
```

The base class carries the `[Column]` attributes, from
`System.ComponentModel.DataAnnotations.Schema` rather than from EF, which is what lets
`Trax.Effect` be referenced without EF Core. `Persistent<Entity>` inherits from it and adds a
static `OnModelCreating` that does the EF mapping through the Fluent API:
`entity.ToTable("manifest", "trax")`, the key, the index, the `jsonb` column types. `[Table]`
is not used in either tree.

Not everything under `Models/` is a table. `Models/Host/TraxHostInfo.cs` is process identity
and `Models/JunctionMetadata/` is in-flight junction state, and neither has a `Persistent*`
counterpart. A folder can also hold more than the entity: `Models/Manifest/` carries
`Manifest.cs`, `Exclusion.cs`, `IManifestProperties.cs` and a `DTOs/` folder. Put the entity,
the interface it satisfies and its DTOs together; do not split them across `Models/`.

Each provider has its own `DbContext` subclass at
`Trax.Effect/src/Trax.Effect.Data.<Provider>/Services/<Provider>Context/<Provider>Context.cs`,
sharing shape through the base `DataContext<TDbContext>` in `Trax.Effect.Data`. The providers
are `Postgres`, `Sqlite` and `InMemory`.

## Migrations

```
Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations/<NNN>_<name>.sql
Trax.Effect/src/Trax.Effect.Data.Sqlite/Migrations/<NNN>_<name>.sql
```

Numbered sequentially from 001 with no gaps, embedded by a csproj glob. The two sets are
numbered independently. `Trax.Effect.Data.InMemory` has no `Migrations/` folder: the EF
in-memory provider stores objects rather than tables, so there is no DDL to run. See
[Writing Migrations](/docs/reference/writing-migrations).

## Builders

A partial class in its own directory, one file per feature area. See
[Builder Pattern](/docs/reference/builder-pattern).

## Sample applications

Most samples are three or four projects under `Trax.Samples/samples/<Topic>/`:

```
Trax.Samples.<Name>            the train library, with [TraxQuery] trains under Trains/
Trax.Samples.<Name>.Api        the GraphQL host (or .Hub)
Trax.Samples.<Name>.Client     the consumer (or .Scheduler, .Worker, .Runner)
```

A sample that demonstrates one thing is one project: `samples/ApiAudit/` and
`samples/SignalRDashboard/` are each a single web host with its trains under `Trains/`, because
neither has anything to say about a second process. Split when a second process has to exist
for the sample to make its point, not before. Bookworm goes the other way with five, because
cross-schema edges need a project of their own.

Resolvers and `[ExtendObjectType]` classes belong in a project, not in `Program.cs`. Which
project depends on what they extend: subscription extensions live in the train library
(`Trax.Samples.ChatService/Subscriptions/`), cross-schema edges in the project that owns the
join (`Trax.Samples.Bookworm.CrossSchema/Edges/`), and an extension over a type the host
defines lives in the host (`Trax.Samples.GameServer.Api/TypeExtensions/`).

## Tests

See [Test Conventions](/docs/reference/test-conventions) for the folder layout and the
fixture patterns.

## When you cannot find a pattern

Ask before improvising. A new pattern introduced without justification is almost always a
missed pattern that already exists.
