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
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceCollection` | Yes | The service collection |

**Returns**: `IServiceCollection` for continued chaining.

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

`UseTraxGraphQL` calls `app.UseWebSockets()` internally to enable the WebSocket transport required for [GraphQL subscriptions]({{ site.baseurl }}{% link sdk-reference/graphql-api/subscriptions.md %}).

## What It Registers

`AddTraxGraphQL` calls `AddTraxApi()` internally (shared API services), then configures HotChocolate:

- **Named GraphQL server** via `AddGraphQLServer("trax")` — uses a named schema so it coexists with your own HotChocolate schemas in the same application
- **Query type**: `RootQuery` with two grouped sub-types:
  - **`discover`** (`DiscoverQueries`) — auto-generated typed query fields for trains annotated with [`[TraxQuery]`]({{ site.baseurl }}{% link sdk-reference/graphql-api/trax-graphql-attribute.md %})
  - **`operations`** (`OperationsQueries`) — predefined operational queries: `health` status, registered `trains` discovery, `manifests`, `manifest`, `manifestGroups`, `executions`, `execution`
- **Mutation type**: `RootMutation` with two grouped sub-types:
  - **`dispatch`** (`DispatchMutations`) — auto-generated typed mutations for trains annotated with [`[TraxMutation]`]({{ site.baseurl }}{% link sdk-reference/graphql-api/trax-graphql-attribute.md %}), with strongly-typed input objects derived from each train's input record. Each train gets a single mutation field (e.g. `banPlayer`) with an optional `mode: ExecutionMode` parameter when both Run and Queue operations are enabled (the default).
  - **`operations`** (`OperationsMutations`) — `triggerManifest`, `disableManifest`, `enableManifest`, `cancelManifest`, `triggerGroup`, `cancelGroup`, `triggerManifestDelayed`
- **Subscription type**: `LifecycleSubscriptions` — real-time [lifecycle events]({{ site.baseurl }}{% link sdk-reference/graphql-api/subscriptions.md %}) via WebSocket (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`)
- **In-memory subscription transport** — HotChocolate's built-in pub/sub for delivering events to WebSocket clients
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
