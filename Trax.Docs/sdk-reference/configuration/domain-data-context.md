---
title: DomainDataContext
description: Reference for DomainDataContext, AddDomainDataContext, EnsureSchemaCreatedAsync and IEntityReference, the helpers for your own one-schema EF Core contexts.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 41
package: Trax.Effect.Data
namespace:
  - Trax.Effect.Data.Services.DomainContext
  - Trax.Effect.Data.Models
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/DomainContext/DomainDataContext.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/DomainContext/IDomainDataContext.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/DomainContext/DomainDataContextServiceCollectionExtensions.cs
  - Trax.Effect/src/Trax.Effect.Data/Models/IEntityReference.cs
---

# DomainDataContext

The base class and registration helpers for your own EF Core data contexts: one project, one schema, one context. A
domain context holds application data, separate from Trax's [IDataContext](/docs/sdk-reference/configuration/i-data-context),
and can share its database.

## Signatures

```csharp fragment
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
    )
        where TInterface : class
        where TContext : DbContext, TInterface;

    public static Task EnsureSchemaCreatedAsync<TContext>(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
        where TContext : DbContext;
}
```

```csharp fragment
namespace Trax.Effect.Data.Models;

public interface IEntityReference;
```

## Members

| Member | Description |
|--------|-------------|
| `Schema` | The one schema this context owns. Every table it maps lands there. |
| `ConfigureModel(ModelBuilder)` | Your mapping: keys, indexes, relationships, cross-schema reads. The base sets the default schema, so it does not call `HasDefaultSchema`. |
| `OnModelCreating` | Sealed. Sets `Schema` as the default schema on PostgreSQL (SQLite and the in-memory provider have none), calls `ConfigureModel`, then makes every `DateTime` property read back as UTC. |
| `Raw()` | This context as the concrete `TSelf`, for code holding the companion interface that needs an EF member the interface does not surface |
| `IDomainDataContext.SaveChangesAsync` | The `DbContext` method, surfaced on the companion interface |
| `IEntityReference` | A marker for a scalar-only interface over an entity another schema owns. A context that reads such an entity exposes it through an `I{Entity}Reference : IEntityReference` that declares only scalar columns, because the entity's navigations are ignored in the reading context and would throw at query time. |

## Parameters

| Method | Parameter | Description |
|--------|-----------|-------------|
| `AddDomainDataContext` | `configureProvider` | Configures the EF provider, for example `o => o.UseNpgsql(connectionString)` |
| `EnsureSchemaCreatedAsync` | `cancellationToken` | Cancels the creation |

## Behavior

### AddDomainDataContext

Registers a pooled context factory for `TContext`, and `TInterface` as a scoped service created from it. A pooled
context is constructed from its options alone, so a context whose constructor takes a scoped service (the caller an
owner-scope query filter reads, for one) cannot be registered this way; register it with
`AddDbContextFactory<TContext>(..., ServiceLifetime.Scoped)` instead. See
[Owner-scoped contexts](/docs/effect/effect-providers/domain-data-contexts#owner-scoped-contexts).

### EnsureSchemaCreatedAsync

Creates the context's schema and whichever of its tables do not exist yet. On a relational provider it creates the
schema if it is missing, checks which of the model's tables exist, and runs the create statements for the rest in one
transaction: each missing table with its keys, indexes, foreign keys and seed rows. On the in-memory provider it calls
`EnsureCreatedAsync`.

- A sequence is checked and created like a table. A statement tied to no table or sequence (a Postgres extension or
  enum) runs only when none of the context's own tables, the ones in its default schema, exist yet.
- On Postgres the check and create hold a session advisory lock, so hosts starting together create everything once.
- A second run over a created schema runs nothing, and a table added to the model later is created on the next start.
  Tables are compared by name only: a column added to an existing table's model is not added.
- Throws the provider's `DbException` when the database refuses a create statement; the transaction rolls back, so no
  table of that run is left behind.
- Throws `InvalidOperationException` when the schema name is not a plain identifier (letters, digits and underscores,
  starting with a letter or underscore).

It is meant for demos and tests; [Moving to EF migrations](/docs/effect/effect-providers/domain-data-contexts#moving-to-ef-migrations)
shows the switch to migrations.

## Example

```csharp compile
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(o => o.UseInMemoryDatabase("catalog"));

public class Book
{
    public int Id { get; set; }
    public string Isbn { get; set; } = "";
}

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
```

## See also

- [Domain Data Contexts](/docs/effect/effect-providers/domain-data-contexts), the conventions, PostgreSQL enum columns and cross-schema reads
- [Architecture Guards](/docs/reference/architecture-guards), the checks that enforce them
