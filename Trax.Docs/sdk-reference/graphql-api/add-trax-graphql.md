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
| `configure` | `Func<TraxGraphQLBuilder, TraxGraphQLBuilder>` | No | Optional builder for registering DbContext-based [query models](/docs/sdk-reference/graphql-api/query-models). |

**Returns**: `IServiceCollection` for continued chaining.

The parameterless overload calls the builder overload with an identity function. Use the builder overload to register DbContexts whose entities are annotated with `[TraxQueryModel]`:

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<GameDbContext>());
```

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
| `app` | `WebApplication` | Yes | — | The built application |
| `routePrefix` | `string` | No | `"/trax/graphql"` | The URL path where the GraphQL endpoint is mapped |
| `configure` | `Action<IEndpointConventionBuilder>?` | No | `null` | Optional callback to apply endpoint conventions (authorization, rate limiting, CORS) to the GraphQL endpoint. |

**Returns**: `WebApplication` for continued chaining.

`UseTraxGraphQL` calls `app.UseWebSockets()` internally to enable the WebSocket transport required for [GraphQL subscriptions](/docs/sdk-reference/graphql-api/subscriptions).

## What It Registers

`AddTraxGraphQL` calls `AddTraxApi()` internally (shared API services), then configures HotChocolate:

- **Named GraphQL server** via `AddGraphQLServer("trax")` — uses a named schema so it coexists with your own HotChocolate schemas in the same application
- **Query type**: `RootQuery` with grouped sub-types:
  - **`operations`** (`OperationsQueries`) — always present. Predefined operational queries: `health` status, registered `trains` discovery, `manifests`, `manifest`, `manifestGroups`, `executions`, `execution`
  - **`discover`** (`DiscoverQueries`) — present when trains annotated with [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) are registered, or when entities annotated with [`[TraxQueryModel]`](/docs/sdk-reference/graphql-api/query-models) are discovered via `AddDbContext<T>()`. Contains auto-generated typed query fields for each query train, and paginated/filterable/sortable fields for each query model.
- **Mutation type**: `RootMutation` with grouped sub-types:
  - **`operations`** (`OperationsMutations`) — always present. `triggerManifest`, `disableManifest`, `enableManifest`, `cancelManifest`, `triggerGroup`, `cancelGroup`, `triggerManifestDelayed`
  - **`dispatch`** (`DispatchMutations`) — only present when trains annotated with [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) are registered. Auto-generated typed mutations with strongly-typed input objects derived from each train's input record. Each train gets a single mutation field (e.g. `banPlayer`) with an optional `mode: ExecutionMode` parameter when both Run and Queue operations are enabled (the default).
- **Subscription type**: `LifecycleSubscriptions` — real-time [lifecycle events](/docs/sdk-reference/graphql-api/subscriptions) via WebSocket (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`)
- **In-memory subscription transport** — HotChocolate's built-in pub/sub for delivering events to WebSocket clients
- **Error filter**: `TraxErrorFilter` — exposes actual exception messages for train-related errors instead of HotChocolate's default "Unexpected Execution Error". Exposed types: `TrainException` (code: `TRAX_TRAIN_ERROR`), `TrainAuthorizationException` (code: `TRAX_AUTHORIZATION`), `InvalidOperationException` (code: `TRAX_INVALID_OPERATION`). All other exception types retain the default masked message.
- **Lifecycle hook**: `GraphQLSubscriptionHook` — automatically registered to publish train state transitions to the subscription transport

## Prerequisites

`AddTraxGraphQL` depends on services registered by `AddMediator()` (provides `ITrainDiscoveryService` and `ITrainExecutionService`) and a configured data context (provides `IDataContextProviderFactory`). These are normally set up through `AddTrax`.

`AddTraxGraphQL` performs a runtime check that `AddTrax()` was called first. If the `TraxMarker` singleton is not found in the DI container, `AddTraxGraphQL` throws `InvalidOperationException`:

```
InvalidOperationException: AddTrax() must be called before AddTraxGraphQL().
Call services.AddTrax(...) in your service configuration before calling AddTraxGraphQL().
```

This ensures the required Trax services are available before the GraphQL schema is built.

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

With authorization:

```csharp
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization("AdminPolicy"));
```

## Package

```
dotnet add package Trax.Api.GraphQL
```
