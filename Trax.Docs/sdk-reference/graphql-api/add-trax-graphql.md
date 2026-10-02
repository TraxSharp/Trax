---
layout: default
title: AddTraxGraphQL
description: "Reference for AddTraxGraphQL and UseTraxGraphQL: builder methods, what they register, prerequisites, registration order and schema download rules."
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
    .ConfigureFiltering(filter => filter.AddCaseInsensitiveStringOperations())
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
| `AddTypeModule<TTypeModule>()` | Registers an additional HotChocolate `TypeModule` on the Trax schema. Use this to add custom resolvers, DataLoader-backed relationship fields, or `ObjectTypeExtension`s on entities already registered by `[TraxQueryModel]`. The module is registered as a singleton in DI. A field it contributes follows the [type-extension posture rule](/docs/authorization#fields-added-by-a-type-extension): `[TraxAuthorize]` on its resolver is a gate, and a field on a root or anonymous parent must declare one. |
| `AddFilterType<TEntity, TFilter>()` | Overrides the auto-generated `FilterInputType` for a specific entity. `TFilter` must extend `FilterInputType<TEntity>`. See [custom filter and sort types](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types). |
| `AddSortType<TEntity, TSort>()` | Overrides the auto-generated `SortInputType` for a specific entity. `TSort` must extend `SortInputType<TEntity>`. See [custom filter and sort types](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types). |
| `ConfigureFiltering(Func<TraxFilterBuilder, TraxFilterBuilder>)` | Layers opt-in operators onto HotChocolate's filter convention. Today: `AddCaseInsensitiveStringOperations()` adds `icontains` and `ieq` to every string filter input. Stock filtering is the default; nothing changes unless this is called. See [configure filtering](/docs/sdk-reference/graphql-api/configure-filtering). |
| `AddTypeExtension<T>()` | Registers a single HotChocolate type extension class (e.g., a class decorated with `[ExtendObjectType]`) on the Trax schema. `T` must be a class. Use this for explicit per-type registration. |
| `AddTypeExtensions(params Assembly[])` | Scans the given assemblies for all non-abstract classes decorated with `[ExtendObjectType]` and registers them on the Trax schema. Mirrors the `AddMediator` assembly-scanning pattern: add a new type extension class and it's auto-discovered. |
| `ConfigureSchema(Action<IRequestExecutorBuilder>)` | Applies arbitrary configuration to the underlying HotChocolate `IRequestExecutorBuilder`. Use this for settings that Trax doesn't expose directly (custom conventions, error handling, etc.). Callbacks run after all standard Trax configuration. |
| `MaxExecutionDepth(int)` | Overrides the default max query depth (default: 15). Queries deeper than this are rejected at validation. Introspection fields are excluded from the count. |
| `ConfigureCost(Action<CostOptions>)` | Adjusts HotChocolate cost-analyzer options on top of Trax defaults (`MaxFieldCost = 1000`, `DefaultResolverCost = 10`). |
| `AllowIntrospection(Predicate<HttpContext>)` | Supplies a per-request predicate that decides whether the schema may be read. Default: allowed in Development, denied elsewhere. Once set, the predicate decides in every environment. It is called with the request's `HttpContext` for every operation on every transport (on a socket, the upgrade request). A denied operation that selects `__schema` or `__type` fails validation with `HC0046`. The schema download and the GraphQL IDE on the `UseTraxGraphQL` endpoint follow the same answer and return 404 when denied. An operation executed in-process, with no HTTP request, is answered by the environment alone. |
| `AllowGetRequests()` | Serves GraphQL queries over HTTP GET. **Off by default**: the endpoint executes only POSTed operations, because a cross-site link carries the user's `SameSite=Lax` cookie and a GET-executable query would run as them. With the opt-in, a GET must still carry the `GraphQL-preflight: 1` header and may run only queries (a mutation over GET is refused). The setting is on the `trax` schema, so it applies however the endpoint is mapped. The IDE page and the SDL download are not affected. |
| `AllowSocketOrigins(params string[] origins)` | Browser origins, besides the endpoint's own host, from which a WebSocket upgrade is accepted. Each value is a scheme, host and optional port (`https://app.example.com`). Replaces the default, which is the origins of the CORS default policy; with no arguments only the endpoint's own host is allowed. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#allowed-origins). |
| `MaxOperationsPerRequest(int)` | Overrides the default per-request operation cap (default: 50). An operation is a root field or a field under a namespace (`dispatch`, `discover`, `operations`, their nested namespaces, a train's declared `Namespace`); the namespace field itself does not count. Aliased fields and batched operations both count, and so do selections reached through fragment spreads and inline fragments; selections sharing a response path merge at execution and count once. Rejects with code `TRAX_TOO_MANY_OPERATIONS`. |
| `MaxOperationsPerConnection(int)` | Overrides how many operations one WebSocket connection runs at once (default: 100). An operation started past it gets a GraphQL error with code `TRAX_SOCKET_OPERATION_LIMIT` and takes no place; the connection stays open, and a place frees when one of its operations completes. A non-positive value throws `ArgumentOutOfRangeException`. |
| `ExposeOperationQueries()` | Adds the `operations` namespace under `RootQuery`, exposing `health`, `trains`, `manifests`, `manifest`, `manifestGroups`, `executions`, `execution`, and the nested `operations.deadLetters` read queries. **Off by default**, since these endpoints reveal the topology and execution history of the deployment: `operations.hosts` reports internal hostnames and per-instance execution counts, `operations.config` the scheduler's settings. Exposing them without a gate fails at startup unless you answer with `GateOperations(policy, roles)`, `RequireAuthorization()` or `AllowAnonymousOperations()`. |
| `ExposeOperationMutations()` | Adds the `operations` namespace under `RootMutation`, exposing `triggerManifest`, `disableManifest`, `enableManifest`, `cancelManifest`, `triggerGroup`, `cancelGroup`, `triggerManifestDelayed`, `setEffectEnabled`, and the nested `operations.deadLetters` requeue/acknowledge mutations. **Off by default**, since these mutations call the scheduler directly and an unauthenticated caller could disrupt scheduled work. Because of that, exposing them without a gate fails at startup unless you answer with `GateOperations(policy, roles)`, `RequireAuthorization()` or `AllowAnonymousOperations()`. |
| `RequireAuthorization(string? policy = null)` | Gates every GraphQL operation behind an authorization policy, over HTTP and over a subscription socket alike; an unauthenticated caller never satisfies it. The GraphQL IDE (HTML GET) and the schema download are not gated by it; they follow `AllowIntrospection`. Pass no argument to use the combined Trax auth policy that every `AddTrax*Auth` extension contributes a scheme to; pass an explicit policy name (e.g. `ApiKeyDefaults.PolicyName`) to require something more specific. Failed checks return a GraphQL error with code `TRAX_AUTHORIZATION` rather than HTTP 401. A socket is checked at `connection_init` and again for every operation it carries. |
| `GateOperations(policy, roles)` | Puts `@authorize` on the `operations` field of both root types, gating the namespace while the rest of the endpoint stays open. This is the answer for a host with public pre-login surfaces, which cannot use `RequireAuthorization()` without taking those surfaces down too. Policy and roles combine exactly as repeated `[TraxAuthorize]` attributes do: policies AND, roles union and OR. A policy or roles is required: a call with neither (or only blank strings) fails at startup, because "any signed-in user" on a host with public sign-up is everyone, and the namespace reads execution inputs, outputs and logs and requeues, cancels and reconfigures work. Calling it with the namespace not exposed also fails at startup, because there is no field to put the gate on. |
| `GateOperationsToAuthenticatedUsers()` | Gates the `operations` namespace to any authenticated caller, with no policy or role. It is the explicit spelling of what a parameterless `GateOperations()` used to do. Use it only when every principal that can authenticate is an operator, such as a host whose only identities are service accounts. |
| `AllowAnonymousOperations()` | Acknowledges that the `operations` namespace is reachable with no gate at all. Exposing the namespace without one otherwise fails at startup, since an unauthenticated control plane is almost always a mistake. Call this only when the surface is protected another way (a private network, a sidecar, ASP.NET endpoint authorization) or is intentionally public. Setting it together with `RequireAuthorization()` or `GateOperations()` is a contradiction and also fails at startup. |

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

The schema download (`GET ?sdl`, `/schema`, `/schema.graphql`) and the GraphQL IDE on this endpoint follow the same per-request decision as introspection (see `AllowIntrospection` above): served in Development, 404 elsewhere unless your predicate allows the request. An allowed download is sent `Cache-Control: private`. A host that maps the schema itself with `MapGraphQL(path, "trax")` skips that per-request check; without a predicate, outside Development, the schema's server options switch both off anyway.

`AddTraxGraphQL` registers an `IStartupFilter` that prepends `app.UseWebSockets()` to the pipeline, so the WebSocket transport required for [GraphQL subscriptions](/docs/sdk-reference/graphql-api/subscriptions) is always in place before endpoint execution. You do not need to call `app.UseWebSockets()` yourself, and the ordering of `UseTraxGraphQL()` relative to other endpoint middleware (such as `UseTraxDashboard()`) does not affect the upgrade.

The Trax schema accepts a WebSocket upgrade only from origins the host serves, whether `UseTraxGraphQL()` maps it or the host maps it with `MapGraphQL(path, "trax")`: no `Origin` header, the endpoint's own host, or an origin allowed by `AllowSocketOrigins(...)` (default: the CORS default policy). Others get `403`. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#allowed-origins).

## What It Registers

`AddTraxGraphQL` calls `AddTraxApi()` internally (shared API services), then configures HotChocolate:

- **Named GraphQL server** via `AddGraphQLServer("trax")`, using a named schema so it coexists with your own HotChocolate schemas in the same application
- **Query type**: `RootQuery` with grouped sub-types:
  - **`operations`** (`OperationsQueries`): opt-in via `ExposeOperationQueries()`. Predefined operational queries: `health` status, registered `trains` discovery, `manifests`, `manifest`, `manifestGroups`, `executions`, `execution`, plus the nested `operations.deadLetters` namespace (`deadLetters`, `deadLetter`).
  - **`discover`** (`DiscoverQueries`): present when trains annotated with [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) are registered, or when entities annotated with [`[TraxQueryModel]`](/docs/sdk-reference/graphql-api/query-models) are discovered via `AddDbContext<T>()`. Contains auto-generated typed query fields for each query train, and paginated/filterable/sortable fields for each query model.
- **Mutation type**: `RootMutation` is registered only when at least one source of mutations exists: a `[TraxMutation]` train, or `ExposeOperationMutations()`. Sub-types:
  - **`operations`** (`OperationsMutations`): opt-in via `ExposeOperationMutations()`. `triggerManifest`, `disableManifest`, `enableManifest`, `cancelManifest`, `triggerGroup`, `cancelGroup`, `triggerManifestDelayed`, `setEffectEnabled`, plus the nested `operations.deadLetters` namespace (`requeueDeadLetter`, `acknowledgeDeadLetter`, batch and "all" variants).
  - **`dispatch`** (`DispatchMutations`): only present when trains annotated with [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) are registered. Auto-generated typed mutations with strongly-typed input objects derived from each train's input record. Each train gets a single mutation field (e.g. `banPlayer`) with an optional `mode: ExecutionMode` parameter when both Run and Queue operations are enabled (the default).

> The Trax operational surface is **off by default**. The scheduler-control mutations (`triggerManifest`, `cancelGroup`, dead-letter requeue, etc.) call `ITraxScheduler` directly, and an unauthenticated caller could disrupt scheduled work. The read queries (`health`, `manifests`, `executions`, `trains`) reveal deployment topology. Both surfaces have to be opted in explicitly via `ExposeOperationQueries()` and `ExposeOperationMutations()`. Exposing either surface without a gate fails at startup, and there are three ways to answer:

> | Answer | What it does |
> | --- | --- |
> | `GateOperations(policy, roles)` | Gates the `operations` namespace alone. The rest of the endpoint stays open, so public trains and query models keep working. A policy or roles is required; `GateOperationsToAuthenticatedUsers()` is the explicit form for "any signed-in caller". |
> | `RequireAuthorization(policy)` | Gates the whole endpoint, `operations` included. |
> | `AllowAnonymousOperations()` | Acknowledges a deliberately public control plane. |
>
> The read queries carry the same requirement as the mutations. `operations.hosts` returns internal hostnames, environment and per-instance execution counts; `operations.config` returns the scheduler's configuration. That is an infrastructure disclosure whether or not the mutations beside it are gated.

Exposing the operations surface changes two behaviours automatically:

- **Startup guard.** The operations resolvers call `IOperationsService` (and, for the mutations, `ITraxScheduler`). If you expose them without registering those services, the schema still builds and every operation fails at request time with a masked error. To catch this at boot instead, `AddTraxGraphQL()` registers a hosted-service guard that throws `InvalidOperationException` at startup when the surface is exposed but the backing services are missing, including `ITrainExecutionService` (registered by `AddMediator(...)`), which the operations enqueue needs, and, when the mutations are exposed, an `IJobSubmitter`, which [`runTrain`](/docs/sdk-reference/graphql-api/mutations#runtrain) hands its run to. A collocated host gets them from `AddScheduler(...)`; an API-only host that talks to a remote scheduler needs `AddTraxJobRunner()` plus `services.AddScoped<IOperationsService, OperationsService>()`, and `AddMediator(...)` registering its trains, because `OperationsService` enqueues `queueTrain` and `requeueExecution` through `ITrainExecutionService`. `AddTraxJobRunner()` registers no job submitter, so an API-only host that exposes the mutations also registers the one its workers read from, `services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()` when they poll PostgreSQL (the scheduler's local workers, or `AddTraxWorker()`); without one the host refuses to start. On such a host a scheduler settings change (`operations.config.updateScheduler`) writes only the fields it names and reaches the running scheduler within seconds, but the first one, which creates the stored settings, must be made on the scheduler host: the API host does not know the scheduler's values for the rest.
- **Stream every train.** Exposing operations marks the host as an administrative/observability surface, so the [lifecycle subscriptions](/docs/sdk-reference/graphql-api/subscriptions) emit events for **all** trains, not just those decorated with [`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute). Without the operations surface, only `[TraxBroadcast]` trains emit (the user-facing default).

If you call `AddTraxGraphQL` with no `[TraxQuery]` trains, no `[TraxQueryModel]` entities, and `ExposeOperationQueries()` not set, the call throws `InvalidOperationException` because the resulting `RootQuery` would have no fields and HotChocolate would refuse to build the schema.
- **Subscription type**: `LifecycleSubscriptions`, providing real-time [lifecycle events](/docs/sdk-reference/graphql-api/subscriptions) via WebSocket (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`)
- **In-memory subscription transport**: HotChocolate's built-in pub/sub for delivering events to WebSocket clients
- **Error filter**: `TraxErrorFilter`, which curates exception messages for train-related errors. Exposed types: a `TrainException` message a train author wrote passes through (`TRAX_TRAIN_ERROR`), and so does the carried message of a `TrainException` a remote worker or nested train reported; a `TrainException` carrying any other exception type (the `TrainExceptionData` JSON a remote worker's failure comes home as), or wrapping a remote endpoint's reply, returns `"The train failed."` (`TRAX_TRAIN_ERROR`) and the detail stays in the metadata row; `TrainAuthorizationException` always returns the generic `"Not authorized."` (`TRAX_AUTHORIZATION`); `TrainNotFoundException` returns `"The requested train was not found."` (`TRAX_TRAIN_NOT_FOUND`); `AmbiguousTrainNameException` surfaces candidate FullNames (`TRAX_AMBIGUOUS_TRAIN`); `TrainInputValidationException` returns `"The train input failed validation."` (`TRAX_INVALID_INPUT`); the mediator's `NoTrainForInputException`, thrown when no registered train takes the input, returns `"The train could not be run."` (`TRAX_HOST_CONFIGURATION`) even with HotChocolate's exception details switched on, and the full exception, naming the input type and the assemblies the host scanned, is logged on the server at Error, since the fix is in how the host is built. All other exception types (including `InvalidOperationException`) retain HotChocolate's default masked message.
- **Hardening defaults**: max execution depth of 15, cost-analyzer budget `MaxFieldCost = 1000`, introspection allowed in Development only (decided per request, see `AllowIntrospection`), and a 50-operation per-request cap. Override each via the builder methods above.
- **Subscription auth interceptor**: Trax registers `TraxCompositeSocketInterceptor` so WebSocket subscriptions authenticate from the `connection_init` payload. It serves API-key auth (`authToken`/`apiKey`), JWT auth (`authToken`/`bearer`, including Authority/JWKS schemes) and the JWT dispatcher, together or alone, and accepts every connection when none is registered. Supply your own via `ConfigureSchema(b => b.AddSocketSessionInterceptor<T>())` to replace it. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#authentication).
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

Execution-only authorization (the IDE shell stays loadable where `AllowIntrospection` allows it, queries and mutations require a key):

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
```

The two are independent. Use the builder method when you want developers to load the IDE without credentials and only enforce auth on actual GraphQL operations; use the endpoint method when even the IDE shell should be gated.

## Registration order

`AddTraxGraphQL()` needs `AddTrax()` to have run first, and every `[TraxQuery]`, `[TraxMutation]`
or `[TraxBroadcast]` train registered before it. A train registered afterwards refuses the host at
startup, naming the train:

```csharp
builder.Services.AddTrax(trax => trax.AddEffects(...).AddMediator(...));
builder.Services.AddTraxJwtAuth(...);        // either side of AddTraxGraphQL
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<AppDbContext>());
```

Authentication may come before or after it. The subscription interceptor reads the registered
schemes once the container is complete, so earlier versions' failure, where an auth call after
`AddTraxGraphQL()` left subscriptions accepting every `connection_init` while HTTP stayed gated,
cannot happen.

The startup checks `AddTraxGraphQL()` registers (an exposed surface missing its services, a posture
contradiction, a train registered after it) run in the hosted-service `StartingAsync` step, which
the host finishes for every service before it starts any. A refused host never binds Kestrel or
starts a worker, even with `HostOptions.ServicesStartConcurrently`.

Everything else is order-independent too. `@authorize` is attached to the schema, so query and
mutation gating works whichever way round the host is composed, and services the GraphQL
components depend on are resolved on first use rather than when `AddTraxGraphQL()` runs.

## Package

```
dotnet add package Trax.Api.GraphQL
```
