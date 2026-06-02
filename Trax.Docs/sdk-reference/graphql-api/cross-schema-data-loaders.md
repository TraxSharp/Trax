---
layout: default
title: Cross-Schema Data Loaders
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 11
---

# Cross-Schema Data Loaders

EF Core cannot JOIN across two `DbContext` instances, so a GraphQL field that resolves an entity owned by a different schema/context has no SQL join available and would otherwise run one query per parent row (N+1). `CrossSchemaLoader<TContext, TEntity>` (in `Trax.Api.GraphQL`) batches those lookups: every key requested in a GraphQL request collapses into a single `WHERE id IN (...)` against the owning context.

## CrossSchemaLoader

A `GreenDonut.BatchDataLoader<int, TEntity>` keyed by integer `Id`. It injects the pooled `IDbContextFactory<TContext>` and creates a short-lived context per batch (a `DbContext` is not thread-safe). You do not subclass it; you reference the closed type in an edge resolver.

```csharp
[ExtendObjectType(typeof(Loan))]
public sealed class LoanToBookEdge
{
    public async Task<Book?> GetBook(
        [Parent] Loan loan,
        CrossSchemaLoader<CatalogDbContext, Book> books,
        CancellationToken ct
    ) => await books.LoadAsync(loan.BookId, ct);
}
```

The target entity must have an integer `Id` primary key. Because HotChocolate projection only loads selected columns, a query selecting the edge must also select its foreign key (here `bookId`).

## CrossSchemaEdge and registration

Declare every edge in one manifest (the single source of truth the [architecture guards](/docs/reference/architecture-guards) verify), and register one loader per distinct target pair:

```csharp
public static readonly IReadOnlyList<CrossSchemaEdge> All =
[
    new(Source: typeof(Loan), Fk: nameof(Loan.BookId), Target: typeof(Book),
        TargetContext: typeof(CatalogDbContext), FieldName: "book"),
];

// In your service registration:
services.AddCrossSchemaLoader<CatalogDbContext, Book>();
```

Cross-schema edge resolvers live in their own project (the one place allowed to reference more than one domain context), keeping the domains isolated from each other.

| Member | Description |
|---|---|
| `CrossSchemaLoader<TContext, TEntity>` | Batched loader resolving `TEntity` (integer `Id` PK) from `TContext`. |
| `CrossSchemaEdge` | Manifest entry: `Source`, `Fk`, `Target`, `TargetContext`, `FieldName`. |
| `AddCrossSchemaLoader<TContext, TEntity>()` | Registers the loader for one target pair. |
