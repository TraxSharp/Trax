---
layout: default
title: AddTraxGraphQL
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 1
---

# AddTraxGraphQL

Registers the Trax GraphQL schema and services using HotChocolate. This adds the query type, mutation type, and all type extensions needed to serve the Trax GraphQL API.

## Signatures

### AddTraxGraphQL

```csharp
public static IServiceCollection AddTraxGraphQL(this IServiceCollection services)
public static IServiceCollection AddTraxGraphQL(
    this IServiceCollection services,
    Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceCollection` | Yes | The service collection |
| `configure` | `Func<TraxGraphQLBuilder, TraxGraphQLBuilder>` | No | Optional builder for registering DbContext-based [query models](/docs/sdk-reference/graphql-api/query-models), custom type modules, filter/sort overrides, and schema configuration. |

**Returns**: `IServiceCollection` for continued chaining.

The parameterless overload calls the builder overload with an identity function. Use the builder overload to configure the schema:

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<GameDbContext>()
    .AddFilterType<Player, PlayerFilterInputType>()
    .AddSortType<Player, PlayerSortInputType>()
    .AddTypeModule<RelationshipTypeModule>()
    .AddTypeExtension<PlayerStatsExtension>()
    .AddTypeExtensions(typeof(PlayerStatsExtension).Assembly)
    .ConfigureSchema(schema => schema
        .ModifyCostOptions(o => { o.MaxFieldCost = 50000; })
    )
);
```

### Builder Methods

| Method | Description |
|--------|-------------|
| `AddDbContext<TDbContext>()` | Registers a DbContext whose `DbSet<T>` entities marked with `[TraxQueryModel]` are exposed as GraphQL queries. See [query models](/docs/sdk-reference/graphql-api/query-models). |
| `AddTypeModule<TTypeModule>()` | Registers an additional HotChocolate `TypeModule` on the Trax schema. Use this to add custom resolvers, DataLoader-backed relationship fields, or `ObjectTypeExtension`s on entities already registered by `[TraxQueryModel]`. The module is registered as a singleton in DI. |
| `AddFilterType<TEntity, TFilter>()` | Overrides the auto-generated `FilterInputType` for a specific entity. `TFilter` must extend `FilterInputType<TEntity>`. See [custom filter and sort types](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types). |
| `AddSortType<TEntity, TSort>()` | Overrides the auto-generated `SortInputType` for a specific entity. `TSort` must extend `SortInputType<TEntity>`. See [custom filter and sort types](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types). |
| `AddTypeExtension<T>()` | Registers a single HotChocolate type extension class (e.g., a class decorated with `[ExtendObjectType]`) on the Trax schema. `T` must be a class. Use this for explicit per-type registration. |
| `AddTypeExtensions(params Assembly[])` | Scans the given assemblies for all non-abstract classes decorated with `[ExtendObjectType]` and registers them on the Trax schema. Mirrors the `AddMediator` assembly-scanning pattern: add a new type extension class and it's auto-discovered. |
| `ConfigureSchema(Action<IRequestExecutorBuilder>)` | Applies arbitrary configuration to the underlying HotChocolate `IRequestExecutorBuilder`. Use this for settings that Trax doesn't expose directly (custom conventions, error handling, etc.). Callbacks run after all standard Trax configuration. |
| `MaxExecutionDepth(int)` | Overrides the default max query depth (default: 4). Queries deeper than this are rejected at validation. Introspection fields are excluded from the count. |
| `ConfigureCost(Action<CostOptions>)` | Adjusts HotChocolate cost-analyzer options on top of Trax defaults (`MaxFieldCost = 1000`, `DefaultResolverCost = 10`). |
| `AllowIntrospection(Predicate<HttpContext>)` | Supplies a per-request predicate that decides whether introspection is allowed. Default: allowed in Development, denied elsewhere. |
| `MaxOperationsPerRequest(int)` | Overrides the default top-level operation cap (default: 50). Aliased fields and batched operations both count. Rejects with code `TRAX_TOO_MANY_OPERATIONS`. |
| `ExposeOperationQueries()` | Adds the `operations` namespace under `RootQuery`, exposing `health`, `trains`, `manifests`, `manifest`, `manifestGroups`, `executions`, `execution`, and the nested `operations.deadLetters` read queries. **Off by default**, since these endpoints reveal the topology and execution history of the deployment. |
| `ExposeOperationMutations()` | Adds the `operations` namespace under `RootMutation`, exposing `triggerManifest`, `disableManifest`, `enableManifest`, `cancelManifest`, `triggerGroup`, `cancelGroup`, `triggerManifestDelayed`, and the nested `operations.deadLetters` requeue/acknowledge mutations. **Off by default**, since these mutations call the scheduler directly and an unauthenticated caller could disrupt scheduled work. |
| `RequireAuthorization(string? policy = null)` | Gates GraphQL HTTP execution behind an authorization policy. The Banana Cake Pop tool page (HTML GET) and schema introspection are governed independently and stay reachable. Pass no argument to use the combined Trax auth policy that every `AddTrax*Auth` extension contributes a scheme to; pass an explicit policy name (e.g. `ApiKeyDefaults.PolicyName`) to require something more specific. Failed checks return a GraphQL error with code `TRAX_AUTHORIZATION` rather than HTTP 401. Subscription auth is governed by the WebSocket interceptor. |

All builder methods return the builder for fluent chaining.

### UseTraxGraphQL

```csharp
public static WebApplication UseTraxGraphQL(
    this WebApplication app,
    string routePrefix = "/trax/graphql",
    Action<IEndpointConventionBuilder>? configure = null
)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `app` | `WebApplication` | Yes | N/A | The built application |
| `routePrefix` | `string` | No | `"/trax/graphql"` | The URL path where the GraphQL endpoint is mapped |
| `configure` | `Action<IEndpointConventionBuilder>?` | No | `null` | Optional callback to apply endpoint conventions (authorization, rate limiting, CORS) to the GraphQL endpoint. |

**Returns**: `WebApplication` for continued chaining.

`UseTraxGraphQL` calls `app.UseWebSockets()` internally to enable the WebSocket transport required for [GraphQL subscriptions](/docs/sdk-reference/graphql-api/subscriptions).

## What It Registers

`AddTraxGraphQL` calls `AddTraxApi()` internally (shared API services), then configures HotChocolate:

- **Named GraphQL server** via `AddGraphQLServer("trax")`, using a named schema so it coexists with your own HotChocolate schemas in the same application
- **Query type**: `RootQuery` with grouped sub-types:
  - **`operations`** (`OperationsQueries`): opt-in via `ExposeOperationQueries()`. Predefined operational queries: `health` status, registered `trains` discovery, `manifests`, `manifest`, `manifestGroups`, `executions`, `execution`, plus the nested `operations.deadLetters` namespace (`deadLetters`, `deadLetter`).
  - **`discover`** (`DiscoverQueries`): present when trains annotated with [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) are registered, or when entities annotated with [`[TraxQueryModel]`](/docs/sdk-reference/graphql-api/query-models) are discovered via `AddDbContext<T>()`. Contains auto-generated typed query fields for each query train, and paginated/filterable/sortable fields for each query model.
- **Mutation type**: `RootMutation` is registered only when at least one source of mutations exists: a `[TraxMutation]` train, or `ExposeOperationMutations()`. Sub-types:
  - **`operations`** (`OperationsMutations`): opt-in via `ExposeOperationMutations()`. `triggerManifest`, `disableManifest`, `enableManifest`, `cancelManifest`, `triggerGroup`, `cancelGroup`, `triggerManifestDelayed`, plus the nested `operations.deadLetters` namespace (`requeueDeadLetter`, `acknowledgeDeadLetter`, batch and "all" variants).
  - **`dispatch`** (`DispatchMutations`): only present when trains annotated with [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) are registered. Auto-generated typed mutations with strongly-typed input objects derived from each train's input record. Each train gets a single mutation field (e.g. `banPlayer`) with an optional `mode: ExecutionMode` parameter when both Run and Queue operations are enabled (the default).

> The Trax operational surface is **off by default**. The scheduler-control mutations (`triggerManifest`, `cancelGroup`, dead-letter requeue, etc.) call `ITraxScheduler` directly, and an unauthenticated caller could disrupt scheduled work. The read queries (`health`, `manifests`, `executions`, `trains`) reveal deployment topology. Both surfaces have to be opted in explicitly via `ExposeOperationQueries()` and `ExposeOperationMutations()`. They are usually paired with `RequireAuthorization()` so only authenticated callers reach them.

If you call `AddTraxGraphQL` with no `[TraxQuery]` trains, no `[TraxQueryModel]` entities, and `ExposeOperationQueries()` not set, the call throws `InvalidOperationException` because the resulting `RootQuery` would have no fields and HotChocolate would refuse to build the schema.
- **Subscription type**: `LifecycleSubscriptions`, providing real-time [lifecycle events](/docs/sdk-reference/graphql-api/subscriptions) via WebSocket (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`)
- **In-memory subscription transport**: HotChocolate's built-in pub/sub for delivering events to WebSocket clients
- **Error filter**: `TraxErrorFilter`, which curates exception messages for train-related errors. Exposed types: `TrainException` passes through (`TRAX_TRAIN_ERROR`); `TrainAuthorizationException` always returns the generic `"Not authorized."` (`TRAX_AUTHORIZATION`); `TrainNotFoundException` returns `"The requested train was not found."` (`TRAX_TRAIN_NOT_FOUND`); `AmbiguousTrainNameException` surfaces candidate FullNames (`TRAX_AMBIGUOUS_TRAIN`); `TrainInputValidationException` returns `"The train input failed validation."` (`TRAX_INVALID_INPUT`). All other exception types (including `InvalidOperationException`) retain HotChocolate's default masked message.
- **Hardening defaults**: max execution depth of 4, cost-analyzer budget `MaxFieldCost = 1000`, introspection allowed in Development only, and a 50-operation per-request cap. Override each via the builder methods above.
- **Subscription auth interceptor**: when `AddTraxApiKeyAuth` is also wired, Trax registers `TraxApiKeySocketInterceptor` so WebSocket subscriptions authenticate via the same `ITraxPrincipalResolver<string>` using an `authToken` (or `apiKey`) key in the `connection_init` payload.
- **Lifecycle hook**: `GraphQLSubscriptionHook`, automatically registered to publish train state transitions to the subscription transport

## Prerequisites

`AddTraxGraphQL` depends on services registered by `AddMediator()` (provides `ITrainDiscoveryService` and `ITrainExecutionService`) and a configured data context (provides `IDataContextProviderFactory`). These are normally set up through `AddTrax`.

`AddTraxGraphQL` performs a runtime check that `AddTrax()` was called first. If the `TraxMarker` singleton is not found in the DI container, `AddTraxGraphQL` throws `InvalidOperationException`:

```
InvalidOperationException: AddTrax() must be called before AddTraxGraphQL().
Call services.AddTrax(...) in your service configuration before calling AddTraxGraphQL().
```

This makes sure the required Trax services are available before the GraphQL schema is built.

## Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddTrax(trax => trax
        .AddEffects(effects => effects
            .UsePostgres(builder.Configuration.GetConnectionString("TraxDatabase")!)
        )
        .AddMediator(ServiceLifetime.Scoped, typeof(Program).Assembly)
    )
    .AddTraxGraphQL();

var app = builder.Build();

app.UseTraxGraphQL(); // serves at /trax/graphql

app.Run();
```

To serve the endpoint at a different path:

```csharp
app.UseTraxGraphQL(routePrefix: "/api/graphql");
```

Endpoint-level authorization (gates *everything* on the route, including the BCP tool page):

```csharp
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization("AdminPolicy"));
```

Execution-only authorization (BCP and introspection stay open, queries and mutations require a key):

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
```

The two are independent. Use the builder method when you want developers to load the IDE without credentials and only enforce auth on actual GraphQL operations; use the endpoint method when even the IDE shell should be gated.

## Package

```
dotnet add package Trax.Api.GraphQL
```
