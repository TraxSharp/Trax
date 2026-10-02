---
layout: default
title: Domain Data Contexts
description: "DomainDataContext, the base for your application's own EF Core context: one schema per context, registration, PostgreSQL enum columns and cross-schema reads."
parent: Effect Providers
grand_parent: Effect
nav_order: 2
---

# Domain Data Contexts

Trax's own `DataContext<T>` is the framework metadata store (it holds the `trax` tables). Your application's data is a separate concern, and `DomainDataContext<TSelf>` (in `Trax.Effect.Data`) is the recommended base for it. It encodes one rule: **one project, one PostgreSQL schema, one context**.

## The base

A domain context derives `DomainDataContext<TSelf>`, declares its single schema, and configures its owned entities. The base seals `OnModelCreating` so the cross-cutting conventions cannot be skipped or reordered: it applies the default schema (on PostgreSQL; schema-less providers like SQLite and the in-memory provider are left alone), runs your `ConfigureModel`, and applies a UTC datetime converter.

```csharp
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DomainDataContext<CatalogDbContext>(options), ICatalogDbContext
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Author> Authors => Set<Author>();

    protected override string Schema => "catalog";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { /* keys, indexes, relationships */ }
}
```

Each context ships a companion `I{Name}DbContext` interface deriving `IDomainDataContext`; application code depends on the interface, never the concrete type.

## Registration and bootstrap

```csharp
// One pooled factory + a scoped resolver bound to the interface.
services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o => o.UseNpgsql(connectionString));

// Create the schema and tables at startup (demo convenience; use migrations in production).
await app.Services.EnsureSchemaCreatedAsync<CatalogDbContext>();
```

`EnsureSchemaCreatedAsync` creates the default schema with `IF NOT EXISTS`, then runs the model's
whole create script and swallows any `DbException` it throws. On a second start the script fails
on its first statement because the tables exist, and that is the steady state. The same swallow
also hides everything else: a table added to the model later is never created (the script stops at
the first table that exists), and any other DDL error, such as a permission failure, passes
silently and surfaces later as a missing table. Once the model changes after its first deployment,
move the context to migrations.

## PostgreSQL enum columns

A C# enum stored as a PostgreSQL enum type is mapped in the `UseNpgsql` options callback, which
is where EF Core's model learns about it:

```csharp
services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o =>
    o.UseNpgsql(connectionString, npgsql => npgsql.MapEnum<BookFormat>("book_format", "catalog")));
```

Passing a connection string, as above, is enough: EF Core builds the data source and carries the
mapping into it. If you build your own `NpgsqlDataSource` and pass that instead, map the enum on
the `NpgsqlDataSourceBuilder` as well, because EF Core cannot change a data source it did not
build. The builder mapping handles the ADO.NET layer and the callback mapping handles the model;
leaving out the callback one fails at runtime with
`column "x" is of type book_format but expression is of type integer`.

```csharp
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<BookFormat>("catalog.book_format");
var dataSource = dataSourceBuilder.Build();

services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o =>
    o.UseNpgsql(dataSource, npgsql => npgsql.MapEnum<BookFormat>("book_format", "catalog")));
```

`UsePostgres` does both for Trax's own enums (`TrainState`, `LogLevel`, `ScheduleType` and the
rest) on the metadata store, so this only concerns enum types your own contexts add.

## Cross-schema reads

A context never references another domain. When it needs to read an entity owned by another schema, the foreign entity exposes a static `OnCrossSchemaModelCreating(ModelBuilder, string schema)` that pins it to the foreign schema and **ignores every navigation**, so EF Core never walks the foreign model graph into the consuming context. The entity is exposed there only through a scalar-only `I{Entity}Reference : IEntityReference` interface, which keeps it out of GraphQL discovery so the owning domain stays the single GraphQL owner. Relationships that cross schemas at the GraphQL layer are resolved by [cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) instead.

These conventions can be enforced in CI with the [architecture guard packages](/docs/reference/architecture-guards).

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Architecture guards](/docs/reference/architecture-guards) | [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context)
