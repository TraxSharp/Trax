---
title: Cross-schema data loaders
description: "Reference for CrossSchemaLoader, CrossSchemaEdge and AddCrossSchemaLoader, which batch GraphQL lookups of entities owned by another DbContext into one query."
type: reference
area: api
package: Trax.Api.GraphQL
namespace: Trax.Api.GraphQL.DataLoaders.CrossSchema
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 9
source:
  - Trax.Api/src/Trax.Api.GraphQL/DataLoaders/CrossSchema/CrossSchemaLoader.cs
  - Trax.Api/src/Trax.Api.GraphQL/DataLoaders/CrossSchema/CrossSchemaEdge.cs
  - Trax.Api/src/Trax.Api.GraphQL/DataLoaders/CrossSchema/CrossSchemaServiceCollectionExtensions.cs
---

# Cross-schema data loaders

`CrossSchemaLoader<TContext, TEntity>` batches the lookups of a GraphQL field that resolves an
entity owned by another `DbContext`. EF Core cannot join across two contexts, so without it such a
field runs one query per parent row; with it, every key requested in one GraphQL request becomes a
single `WHERE id IN (...)` against the owning context. `CrossSchemaEdge` is the manifest entry that
declares such an edge, and `AddCrossSchemaLoader` registers a loader.

## Signatures

```csharp fragment
public sealed class CrossSchemaLoader<TContext, TEntity> : BatchDataLoader<int, TEntity>
    where TContext : DbContext
    where TEntity : class

public sealed record CrossSchemaEdge(
    Type Source,
    string Fk,
    Type Target,
    Type TargetContext,
    string FieldName);

public static IServiceCollection AddCrossSchemaLoader<TContext, TEntity>(this IServiceCollection services)
    where TContext : DbContext
    where TEntity : class
```

## Members

| Member | Description |
|---|---|
| `CrossSchemaLoader<TContext, TEntity>` | A GreenDonut `BatchDataLoader<int, TEntity>` keyed by the target's integer `Id`. It takes the pooled `IDbContextFactory<TContext>` and creates a short-lived context per batch. It is used closed, as a resolver parameter, not subclassed. |
| `CrossSchemaEdge` | A manifest entry: `Source` (the parent entity), `Fk` (its foreign key property), `Target` (the entity resolved), `TargetContext` (the context that owns it), `FieldName` (the GraphQL field). |
| `AddCrossSchemaLoader<TContext, TEntity>()` | On `CrossSchemaServiceCollectionExtensions`: registers the loader for one target pair. |

## Behavior

The target entity has an integer `Id` primary key.

The foreign key reaches the resolver only when the resolver's `[Parent]` declares it with
`requires:`. Projection narrows the `SELECT` to the columns the caller named; Trax adds a query
model's key to the projection on its own, but a foreign key has to be declared, or `loan.BookId`
arrives as `0` and the edge resolves to null. See
[TraxQueryModel](/docs/sdk-reference/graphql-api/query-models#projection-and-hand-written-resolvers).

The `ExtensionResolversDeclareParentRequirements` guard fails the build when an edge resolver
omits it. It parses each source file and checks each resolver on its own: the reads are the
member accesses on that resolver's `[Parent]` parameter (`loan.BookId`, `loan?.BookId`,
`loan!.BookId`; a method call is not a read), and the declaration is that parameter's `requires:`,
written as `nameof(Loan.BookId)` or as a string naming the field (`"bookId"`). A type extension is
found in any form: `[ExtendObjectType]` anywhere in an attribute list, with its namespace or a
type argument, or HotChocolate's source-generated `[ObjectType<Loan>] static partial class`.
`EdgeResolversUseLoader` checks each public, non-`[GraphQLIgnore]` resolver of a type extension in
a `.CrossSchema` project for a `CrossSchemaLoader<,>`, so one batched resolver does not cover an
ad-hoc query beside it.

`CrossSchemaGuardFixture` (Trax.Api.GraphQL.Testing) is the NUnit fixture that runs these guards:
a test class derives from it and overrides `Edges` (and `Options` when the source roots differ).
Its tests are `Cross_schema_edge_manifest_is_valid`, `Cross_schema_edge_resolvers_use_the_batched_loader`
and `Extension_resolvers_declare_what_they_read_off_their_parent`; `AssertInspected` is the helper
they share, which fails when a guard inspected nothing it was expected to.

The edges are declared in one manifest, which the
[architecture guards](/docs/reference/architecture-guards) verify, with one loader registered per
distinct target pair. The edge resolvers live in a project of their own, the one place that
references more than one domain context.

## Examples

An edge resolver:

```csharp compile
[ExtendObjectType(typeof(Loan))]
public sealed class LoanToBookEdge
{
    [TraxAuthorize]
    public async Task<Book?> GetBook(
        [Parent(requires: nameof(Loan.BookId))] Loan loan,
        CrossSchemaLoader<CatalogDbContext, Book> books,
        CancellationToken ct
    ) => await books.LoadAsync(loan.BookId, ct);
}
```

The manifest entry and the registration:

```csharp compile
public static class CrossSchemaEdges
{
    public static readonly IReadOnlyList<CrossSchemaEdge> All =
    [
        new(Source: typeof(Loan), Fk: nameof(Loan.BookId), Target: typeof(Book),
            TargetContext: typeof(CatalogDbContext), FieldName: "book"),
    ];
}
```

```csharp compile
services.AddCrossSchemaLoader<CatalogDbContext, Book>();
```

## See also

- [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models)
- [Architecture guards](/docs/reference/architecture-guards)
