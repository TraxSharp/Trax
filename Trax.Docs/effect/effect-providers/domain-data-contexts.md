---
layout: default
title: Domain Data Contexts
parent: Effect Providers
grand_parent: Effect
nav_order: 2
---

# Domain Data Contexts

Trax's own `DataContext<T>` is the framework metadata store (it holds the `trax` tables). Your application's data is a separate concern, and `DomainDataContext<TSelf>` (in `Trax.Effect.Data`) is the recommended base for it. It encodes one rule: **one project, one PostgreSQL schema, one context**.

## The base

A domain context derives `DomainDataContext<TSelf>`, declares its single schema, and configures its owned entities. The base seals `OnModelCreating` so the cross-cutting conventions cannot be skipped or reordered: it applies the default schema (on PostgreSQL; schema-less providers like SQLite and the in-memory provider are left alone), runs your `ConfigureModel`, and applies a UTC datetime converter.

```csharp
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

// Create the schema and tables idempotently at startup (demo convenience; use migrations in production).
await app.Services.EnsureSchemaCreatedAsync<CatalogDbContext>();
```

## Cross-schema reads

A context never references another domain. When it needs to read an entity owned by another schema, the foreign entity exposes a static `OnCrossSchemaModelCreating(ModelBuilder, string schema)` that pins it to the foreign schema and **ignores every navigation**, so EF Core never walks the foreign model graph into the consuming context. The entity is exposed there only through a scalar-only `I{Entity}Reference : IEntityReference` interface, which keeps it out of GraphQL discovery so the owning domain stays the single GraphQL owner. Relationships that cross schemas at the GraphQL layer are resolved by [cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) instead.

These conventions can be enforced in CI with the [architecture guard packages](/docs/reference/architecture-guards).

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Architecture guards](/docs/reference/architecture-guards)
