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

## What It Registers

`AddTraxGraphQL` calls `AddTraxApi()` internally (shared API services), then configures HotChocolate:

- **Named GraphQL server** via `AddGraphQLServer("trax")` — uses a named schema so it coexists with your own HotChocolate schemas in the same application
- **Query type**: `TrainQueries` — registered as the root query type (includes `health` query)
- **Mutation type**: empty root created via `AddMutationType()`, extended by:
  - `TrainTypeModule` — auto-generates typed `run{TrainName}` and `queue{TrainName}` mutations for every registered train, with strongly-typed input objects derived from each train's input record
  - `TrainMutations` — generic `queueTrain`, `runTrain` (accepts any train by name with untyped JSON input)
  - `SchedulerMutations` — `triggerManifest`, `disableManifest`, `enableManifest`, `cancelManifest`, `triggerGroup`, `cancelGroup`, `triggerManifestDelayed`

## Prerequisites

`AddTraxGraphQL` depends on services registered by `AddServiceTrainBus()` (provides `ITrainDiscoveryService` and `ITrainExecutionService`) and a configured data context (provides `IDataContextProviderFactory`). These are normally set up through `AddTraxEffects`.

## Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddTraxEffects(options => options
        .AddPostgresEffect(builder.Configuration.GetConnectionString("TraxDatabase")!)
        .AddServiceTrainBus(ServiceLifetime.Scoped, typeof(Program).Assembly)
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
