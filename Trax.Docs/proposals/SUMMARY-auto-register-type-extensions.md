# Proposal: Auto-register `[ExtendObjectType]` classes by assembly scan

## Problem

Consumers must manually register every `[ExtendObjectType]` class with individual `AddTypeExtension<T>()` calls on the raw HotChocolate builder:

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<ClientDataContext>());
builder.Services.AddGraphQLServer("trax")
    .AddTypeExtension<BillTypeExtension>()
    .AddTypeExtension<CampaignTypeExtension>()
    .AddTypeExtension<CommitteeTypeExtension>()
    // ... 7 lines, grows with every new extension
```

This is inconsistent with `AddMediator(assembly)` which auto-discovers all trains and junctions. Every new type extension requires a Program.cs change.

## Current state in Trax

- `TraxGraphQLBuilder` has `AddTypeModule<T>()` (stores in a list, registered via `MakeGenericMethod` in `GraphQLServiceExtensions.cs` lines 120-121) — but this is for full `TypeModule` implementations, not `[ExtendObjectType]` classes
- `ConfigureSchema(Action<IRequestExecutorBuilder>)` gives raw builder access but no assembly scan helper
- The ChatService sample uses the same manual pattern
- HotChocolate's `AddTypeExtension` has no `Type`-parameter overload — only `AddTypeExtension<T>()`, requiring `MakeGenericMethod` for reflection-based registration

## Proposed API

### Option A: Per-type on TraxGraphQLBuilder (minimal change)

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<ClientDataContext>()
    .AddTypeExtension<BillTypeExtension>()
    .AddTypeExtension<CampaignTypeExtension>()
);
```

Implementation: store types in a `List<Type>` on `TraxGraphQLBuilder`, then in `GraphQLServiceExtensions.AddTraxGraphQL` iterate and call `AddTypeExtension<T>()` via `MakeGenericMethod` — same pattern as `AdditionalTypeModules`.

### Option B: Assembly-scanning (preferred, mirrors AddMediator)

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<ClientDataContext>()
    .AddTypeExtensions(typeof(BillTypeExtension).Assembly)
);
```

Implementation: scan the assembly for all classes decorated with `[ExtendObjectType]`, collect them, then register each via `MakeGenericMethod`. Mirrors the `AddMediator(assembly)` pattern.

### Option C: Piggyback on AddMediator assemblies

If `AddMediator` already receives the assemblies containing type extensions, Trax could scan those same assemblies for `[ExtendObjectType]` automatically — zero additional API surface.

## Where to implement

- `Trax.Api.GraphQL/Builder/TraxGraphQLBuilder.cs` — add storage + builder method
- `Trax.Api.GraphQL/Extensions/GraphQLServiceExtensions.cs` — add registration loop (after line ~121 where `AdditionalTypeModules` are registered)

## Reference

- `MakeGenericMethod` pattern already used: `GraphQLServiceExtensions.cs` lines 31-38 (TypeModule registration)
- `AddMediator` assembly scanning: `TraxMediatorBuilder.cs`
- HotChocolate source: `AddTypeExtension<T>()` on `IRequestExecutorBuilder`
