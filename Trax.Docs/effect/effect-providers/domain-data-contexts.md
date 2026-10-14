---
title: Add a domain data context
description: "Add your own EF Core context on DomainDataContext: one schema per context, its companion interface, registration, enum columns and cross-schema reads."
type: how-to
area: data
nav_order: 1
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/DomainContext
---

# Add a domain data context

Trax's own `DataContext<T>` holds the `trax` tables. Your application's data goes in a context of its own, derived from `DomainDataContext<TSelf>` (in `Trax.Effect.Data`), which holds to one rule: one project, one PostgreSQL schema, one context. The members of the base are on [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context).

If the context's rows belong to users and every query must be narrowed to the caller, it is registered differently: see [Owner-scoped contexts](#owner-scoped-contexts) before step 3.

## 1. Declare the companion interface

Application code depends on an `I{Name}DbContext` interface deriving `IDomainDataContext`, never on the concrete type. Put it in its own file, `I{Name}DbContext.cs`, in the same directory as the context: the `CompanionInterfaces` guard (`Trax.Effect.Data.Testing`) looks for that file next to the context and fails when the interface is declared inside the context's file.

```csharp compile=catalog,enum,datasource
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

public interface ICatalogDbContext : IDomainDataContext
{
    DbSet<Book> Books { get; }
    DbSet<Author> Authors { get; }
}
```

## 2. Derive the context

Declare its one schema and configure its entities in `ConfigureModel`. The base seals `OnModelCreating`: it applies the default schema (on PostgreSQL; SQLite and the in-memory provider have none), runs your `ConfigureModel`, and applies a UTC datetime converter.

```csharp compile=catalog,enum,datasource
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

## 3. Register it and create its tables

```csharp compile=catalog
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

// One pooled factory, and a scoped resolver bound to the interface.
services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o => o.UseNpgsql(connectionString));

// Create the schema and its tables at startup.
await app.Services.EnsureSchemaCreatedAsync<CatalogDbContext>();
```

`EnsureSchemaCreatedAsync` creates missing tables by name and never alters one that exists ([what it does on each start](/docs/effect/domain-schema-bootstrap)). Once a table's shape changes after its first deployment, [move the context to EF migrations](#moving-to-ef-migrations).

To serve the context's `[TraxQueryModel]` entities over GraphQL, name the concrete type on the GraphQL builder: `AddTraxGraphQL(graphql => graphql.AddDbContext<CatalogDbContext>())`. The pooled factory is all it needs; see [Query Models](/docs/sdk-reference/graphql-api/query-models).

## 4. Map PostgreSQL enum columns (if any)

A C# enum stored as a PostgreSQL enum type is mapped in the `UseNpgsql` options callback, which is where EF Core's model learns about it:

```csharp compile=enum
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o =>
    o.UseNpgsql(connectionString, npgsql => npgsql.MapEnum<BookFormat>("book_format", "catalog")));
```

Passing a connection string, as above, is enough: EF Core builds the data source and carries the mapping into it. If you build your own `NpgsqlDataSource`, map the enum on the `NpgsqlDataSourceBuilder` as well, because EF Core cannot change a data source it did not build. The builder mapping handles the ADO.NET layer and the callback mapping handles the model; leaving out the callback one fails at runtime with `column "x" is of type book_format but expression is of type integer`.

```csharp compile=datasource
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Effect.Data.Services.DomainContext;

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<BookFormat>("catalog.book_format");
var dataSource = dataSourceBuilder.Build();

services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o =>
    o.UseNpgsql(dataSource, npgsql => npgsql.MapEnum<BookFormat>("book_format", "catalog")));
```

`UsePostgres` maps Trax's own enums (`TrainState`, `LogLevel`, `ScheduleType` and the rest) on the metadata store, so this is only for enum types your own contexts add.

## 5. Read another schema's entity (if needed)

A context never references another domain. To read an entity another schema owns, the foreign entity exposes a static `OnCrossSchemaModelCreating(ModelBuilder, string schema)` that pins it to the foreign schema and ignores every navigation, so EF Core never walks the foreign model into the consuming context. The consuming context sees it only through a scalar-only `I{Entity}Reference : IEntityReference` interface, which keeps it out of GraphQL discovery so the owning domain stays the one GraphQL owner. Relationships that cross schemas at the GraphQL layer are resolved by [cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) instead.

The [architecture guard packages](/docs/reference/architecture-guards) enforce these conventions in CI.

## Moving to EF migrations

`EnsureSchemaCreatedAsync` suits a first deployment and tests. [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) replaces it with ordinary EF Core migrations.

## Owner-scoped contexts

A context that narrows every query to the caller with an EF query filter cannot use `AddDomainDataContext`, because a pooled context is built from its options alone. [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) registers it instead.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Architecture guards](/docs/reference/architecture-guards)
