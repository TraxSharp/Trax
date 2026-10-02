---
layout: default
title: DomainDataContext
description: Reference for DomainDataContext, AddDomainDataContext, EnsureSchemaCreatedAsync and IEntityReference, the helpers for your own one-schema EF Core contexts.
parent: Configuration
grand_parent: SDK Reference
nav_order: 19
---

# DomainDataContext

The base class and registration helpers for your own EF Core data contexts: one project, one schema, one context. A domain context is ordinary application data, separate from Trax's [IDataContext](/docs/sdk-reference/configuration/i-data-context); it can share the database.

## Signatures

```csharp
namespace Trax.Effect.Data.Services.DomainContext;

public abstract class DomainDataContext<TSelf> : DbContext, IDomainDataContext
    where TSelf : DbContext
{
    protected DomainDataContext(DbContextOptions<TSelf> options);

    protected abstract string Schema { get; }
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder);

    public TSelf Raw();
}

public interface IDomainDataContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public static class DomainDataContextServiceCollectionExtensions
{
    public static IServiceCollection AddDomainDataContext<TInterface, TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureProvider
    ) where TContext : DbContext, TInterface where TInterface : class;

    public static Task EnsureSchemaCreatedAsync<TContext>(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    ) where TContext : DbContext;
}
```

```csharp
namespace Trax.Effect.Data.Models;

public interface IEntityReference;
```

## DomainDataContext members

| Member | Description |
|--------|-------------|
| `Schema` | The one schema this context owns. Every table it maps lands there. |
| `ConfigureModel(ModelBuilder)` | Your mapping: keys, indexes, relationships, cross-schema reads. Do not call `HasDefaultSchema`; the base owns it. |
| `OnModelCreating` | Sealed. Sets `Schema` as the default schema on PostgreSQL (SQLite and the in-memory provider have none), calls `ConfigureModel`, then makes every `DateTime` property read back as UTC. |
| `Raw()` | This context as the concrete `TSelf`, for code holding the companion interface that needs an EF member the interface does not surface |

## AddDomainDataContext

Registers a pooled context factory for `TContext`, and `TInterface` as a scoped service created from it. Application code injects the interface.

| Parameter | Type | Description |
|-----------|------|-------------|
| `configureProvider` | `Action<DbContextOptionsBuilder>` | Configures the EF provider, for example `o => o.UseNpgsql(connectionString)` |

## EnsureSchemaCreatedAsync

Creates the context's schema and tables at startup, for demos and tests. On a relational provider it creates the schema if it is missing and then runs the model's create script; on the in-memory provider it calls `EnsureCreatedAsync`. The create script has no `IF NOT EXISTS`, so on every run after the first it fails on its first statement, and that `DbException` is swallowed. Use migrations in production: a table added to the model after the first run is never created by this method, and any other database error from the script is swallowed the same way.

Throws `InvalidOperationException` when the schema name is not a plain identifier (letters, digits and underscores, starting with a letter or underscore).

## IEntityReference

A marker for a scalar-only interface over an entity another schema owns. A context that reads such an entity exposes it through an `I{Entity}Reference : IEntityReference` that declares only scalar columns, because the entity's navigations are ignored in the reading context and would throw at query time.

## Example

```csharp
public interface ICatalogDbContext : IDomainDataContext
{
    DbSet<Book> Books { get; }
}

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DomainDataContext<CatalogDbContext>(options), ICatalogDbContext
{
    public DbSet<Book> Books => Set<Book>();

    protected override string Schema => "catalog";

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Book>().HasIndex(b => b.Isbn).IsUnique();
}

services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o => o.UseNpgsql(connectionString));
```

See [Domain Data Contexts](/docs/effect/effect-providers/domain-data-contexts) for the conventions, PostgreSQL enum columns and cross-schema reads, and [Architecture Guards](/docs/reference/architecture-guards) for the checks that enforce them.

## Package

```
dotnet add package Trax.Effect.Data
```
