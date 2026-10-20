---
title: AddTraxGraphQL
description: "Reference for AddTraxGraphQL and UseTraxGraphQL: builder methods, registered services, the operations gate, prerequisites and registration order."
type: reference
area: api
package: Trax.Api.GraphQL
namespace:
  - Trax.Api.GraphQL.Extensions
  - Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 1
source:
  - Trax.Api/src/Trax.Api.GraphQL/Extensions/GraphQLServiceExtensions.cs
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/TraxGraphQLBuilder
  - Trax.Api/src/Trax.Api.GraphQL/Configuration/GraphQLConfiguration.cs
---

# AddTraxGraphQL

Registers the Trax GraphQL schema and its services on HotChocolate: the query, mutation and
subscription types and every type extension the Trax API needs. `UseTraxGraphQL` maps the
endpoint. Both are on `GraphQLServiceExtensions`.

## Signatures

```csharp fragment
public static IServiceCollection AddTraxGraphQL(this IServiceCollection services)

public static IServiceCollection AddTraxGraphQL(
    this IServiceCollection services,
    Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure)

public static WebApplication UseTraxGraphQL(
    this WebApplication app,
    string routePrefix = "/trax/graphql",
    Action<IEndpointConventionBuilder>? configure = null)
```

## Parameters

### AddTraxGraphQL

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceCollection` | Yes | The service collection |
| `configure` | `Func<TraxGraphQLBuilder, TraxGraphQLBuilder>` | No | Configures the schema: [query models](/docs/sdk-reference/graphql-api/query-models), type modules, filter and sort overrides, limits, the operations surface. The parameterless overload passes the builder through unchanged. |

### UseTraxGraphQL

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `app` | `WebApplication` | Yes | N/A | The built application |
| `routePrefix` | `string` | No | `"/trax/graphql"` | The URL path where the GraphQL endpoint is mapped |
| `configure` | `Action<IEndpointConventionBuilder>?` | No | `null` | Applies endpoint conventions (authorization, rate limiting, CORS) to the GraphQL endpoint. |

### Builder methods

Every method on `TraxGraphQLBuilder` returns the builder.

| Method | Description |
|--------|-------------|
| `AddDbContext<TDbContext>()` | Registers a DbContext whose `DbSet<T>` entities marked with `[TraxQueryModel]` are exposed as GraphQL queries. See [query models](/docs/sdk-reference/graphql-api/query-models). |
| `AddTypeModule<TTypeModule>()` | Registers an additional HotChocolate `TypeModule` on the Trax schema. The module can add resolvers, DataLoader-backed relationship fields, or `ObjectTypeExtension`s on entities registered by `[TraxQueryModel]`. The module is registered as a singleton in DI. A field it contributes follows the [type-extension posture rule](/docs/authorization#fields-added-by-a-type-extension): `[TraxAuthorize]` on its resolver is a gate, and a field on a root or anonymous parent must declare one. |
| `AddFilterType<TEntity, TFilter>()` | Overrides the auto-generated `FilterInputType` for a specific entity. `TFilter` must extend `FilterInputType<TEntity>`. See [custom filter and sort types](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types). |
| `AddSortType<TEntity, TSort>()` | Overrides the auto-generated `SortInputType` for a specific entity. `TSort` must extend `SortInputType<TEntity>`. See [custom filter and sort types](/docs/sdk-reference/graphql-api/query-models#custom-filter-and-sort-types). |
| `ConfigureFiltering(Func<TraxFilterBuilder, TraxFilterBuilder>)` | Layers opt-in operators onto HotChocolate's filter convention. Today: `AddCaseInsensitiveStringOperations()` adds `icontains` and `ieq` to every string filter input. Stock filtering is the default; nothing changes unless this is called. See [configure filtering](/docs/sdk-reference/graphql-api/configure-filtering). |
| `AddTypeExtension<T>()` | Registers a single HotChocolate type extension class (e.g., a class decorated with `[ExtendObjectType]`) on the Trax schema. `T` must be a class. The host refuses to start when the type it extends is not in the schema: `Query`, `Mutation` and `Subscription` are HotChocolate's default names, and Trax's roots are `RootQuery`, `RootMutation` (present only when a mutation is exposed) and `LifecycleSubscriptions`. |
| `AddTypeExtensions(params Assembly[])` | Scans the given assemblies for all non-abstract classes decorated with `[ExtendObjectType]` and registers them on the Trax schema. |
| `ConfigureSchema(Action<IRequestExecutorBuilder>)` | Applies arbitrary configuration to the underlying HotChocolate `IRequestExecutorBuilder`. Callbacks run after all standard Trax configuration. |
| `MaxExecutionDepth(int)` | Overrides the default max query depth (default: 15). Queries deeper than this are rejected at validation. Introspection fields are excluded from the count. |
| `ConfigureCost(Action<CostOptions>)` | Adjusts HotChocolate cost-analyzer options on top of Trax defaults (`MaxFieldCost = 1000`, `DefaultResolverCost = 10`). |
| `AllowIntrospection(Predicate<HttpContext>)` | Supplies a per-request predicate that decides whether the schema may be read. Default: allowed in Development, denied elsewhere. Once set, the predicate decides in every environment. It is called with the request's `HttpContext` for every operation on every transport (on a socket, the upgrade request). A denied operation that selects `__schema` or `__type` fails validation with `HC0046`. The schema download and the GraphQL IDE on the `UseTraxGraphQL` endpoint follow the same answer and return 404 when denied. An operation executed in-process, with no HTTP request, is answered by the environment alone. |
| `AllowGetRequests()` | Serves GraphQL queries over HTTP GET. **Off by default**: the endpoint executes only POSTed operations, because a cross-site link carries the user's `SameSite=Lax` cookie and a GET-executable query would run as them. With the opt-in, a GET must still carry the `GraphQL-preflight: 1` header and may run only queries (a mutation over GET is refused). The setting is on the `trax` schema, so it applies however the endpoint is mapped. The IDE page and the SDL download are not affected. |
| `AllowSocketOrigins(params string[] origins)` | Browser origins, besides the endpoint's own host, from which a WebSocket upgrade is accepted. Each value is a scheme, host and optional port (`https://app.example.com`). Replaces the default, which is the origins the CORS default policy names with `WithOrigins(...)` or admits with `SetIsOriginAllowed(...)` (a policy allowing any origin admits none); with no arguments only the endpoint's own host is allowed. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#allowed-origins). |
| `MaxOperationsPerRequest(int)` | Overrides the default per-request operation cap (default: 50). An operation is a root field or a field under a namespace (`dispatch`, `discover`, `operations`, their nested namespaces, a train's declared `Namespace`); the namespace field itself does not count. Aliased fields and batched operations both count, and so do selections reached through fragment spreads and inline fragments; selections sharing a response path merge at execution and count once. Rejects with code `TRAX_TOO_MANY_OPERATIONS`. |
| `MaxOperationsPerConnection(int)` | Overrides how many operations one WebSocket connection runs at once (default: 100). An operation started past it gets a GraphQL error with code `TRAX_SOCKET_OPERATION_LIMIT` and takes no place; the connection stays open, and a place frees when one of its operations completes. A non-positive value throws `ArgumentOutOfRangeException`. |
| `AllowJunctionAnswersForBroadcastSubscribers()` | Lets subscribers outside the operations view see the `answer` and `confidence` of a run's routing steps on [`onJunctionEvent`](/docs/sdk-reference/graphql-api/subscriptions#onjunctionevent), and the names of the junctions on their tracks. **Off by default**: a broadcast subscriber sees that a question was asked, its key and how many steps followed, but not what was answered or which junctions ran on the track, because either can say as much about a run as its output. The operations view always sees answers, and an answer to a question about a `[TraxSensitive]` type is never published to anyone. Surfaced as `TrainLifecycleStreamOptions.IncludeJunctionAnswersForBroadcastSubscribers`. |
| `MaxConnectionLifetime(TimeSpan)` | How long one WebSocket connection stays open before it is closed with code 1001 (Going Away), which graphql-ws clients reconnect after (default: 1 hour). Applies to every connection; a JWT or cookie connection closes earlier when its credential expires. A value that is not positive, or is longer than one day, throws `ArgumentOutOfRangeException`. See [Connection lifetime](/docs/sdk-reference/graphql-api/subscriptions#connection-lifetime). |
| `RecheckConnectionCredentialsEvery(TimeSpan)` | How often an API-key WebSocket connection's key is resolved again (default: 5 minutes). The connection is closed with code 1008 once the key no longer resolves to the principal it connected as, or the resolver fails. Same bounds as `MaxConnectionLifetime`. |
| `ShareEnumWithMachines<TEnum>()` | Lets a field outside the `machines` root carry an enum a served machine also holds: a train's, a query model's or a type extension's. A machine's enum is spelled on the wire by its C# member names (`VeryHigh`), and GraphQL has one type per enum, so every field that carries it is spelled that way, a train's `VERY_HIGH` included. Without the call such a host refuses to start, naming the enum and the field (or the machine and the train). Give one side its own enum instead when that field's clients must keep the old spelling. Trax's own enums, such as `TrainState`, are never shared: `onTrainStateChanged` and the `operations` filters keep their spelling, naming one here throws, and a machine holding one refuses the start with or without the call. Give the machine its own enum. An enum counts as Trax's own when its assembly is named `Trax` or `Trax.*` (not `Trax.Samples.*` or a test project), a consumer assembly named that way included. See [Machines](/docs/sdk-reference/graphql-api/machines). |
| `ExposeOperationQueries()` | Adds the `operations` namespace under `RootQuery`, with the fields listed on [Queries](/docs/sdk-reference/graphql-api/queries#operations-queries). **Off by default**, since these endpoints reveal the topology and execution history of the deployment: `operations.hosts` reports internal hostnames and per-instance execution counts, `operations.config` the scheduler's settings. Exposing them without a gate fails at startup unless you answer with `GateOperations(policy, roles)`, `RequireAuthorization()` or `AllowAnonymousOperations()`. |
| `ExposeOperationMutations()` | Adds the `operations` namespace under `RootMutation`, with the fields listed on [Mutations](/docs/sdk-reference/graphql-api/mutations#operations-mutations). **Off by default**, since these mutations call the scheduler directly and an unauthenticated caller could disrupt scheduled work. Because of that, exposing them without a gate fails at startup unless you answer with `GateOperations(policy, roles)`, `RequireAuthorization()` or `AllowAnonymousOperations()`. |
| `RequireAuthorization(string? policy = null)` | Gates every GraphQL operation behind an authorization policy, over HTTP and over a subscription socket alike; an unauthenticated caller never satisfies it. The GraphQL IDE (HTML GET) and the schema download are not gated by it; they follow `AllowIntrospection`. Pass no argument to use the combined Trax auth policy that every `AddTrax*Auth` extension contributes a scheme to; pass an explicit policy name (e.g. `ApiKeyDefaults.PolicyName`) to require something more specific. Failed checks return a GraphQL error with code `TRAX_AUTHORIZATION` rather than HTTP 401. A socket is checked at `connection_init` and again for every operation it carries. |
| `GateOperations(policy, roles)` | Puts `@authorize` on the `operations` field of both root types, gating the namespace while the rest of the endpoint stays open. The rest of the endpoint stays open, so public trains and query models keep working. Repeated calls combine exactly as repeated `[TraxAuthorize]` attributes do: each call is a requirement of its own and the caller must meet every one, so `GateOperations(roles: "Admin").GateOperations(roles: "Support")` requires both roles, while `GateOperations(roles: "Admin,Support")` accepts either. A policy or roles is required: a call with neither (or only blank strings), or with a role list that names no role (`","`), fails at startup, because "any signed-in user" on a host with public sign-up is everyone, and the namespace reads execution inputs, outputs and logs and requeues, cancels and reconfigures work. Calling it with the namespace not exposed also fails at startup, because there is no field to put the gate on. |
| `GateOperationsToAuthenticatedUsers()` | Gates the `operations` namespace to any authenticated caller, with no policy or role. It is the explicit spelling of what a parameterless `GateOperations()` used to do. |
| `AllowAnonymousOperations()` | Acknowledges that the `operations` namespace is reachable with no gate at all. Exposing the namespace without one otherwise fails at startup, since an unauthenticated control plane is almost always a mistake. Setting it together with `RequireAuthorization()` or `GateOperations()` is a contradiction and also fails at startup. With no gate on the namespace, a field a type extension adds to one of its types inherits nothing and must declare `[TraxAuthorize]` or `[TraxAllowAnonymous]`, as on a root type. |

## Returns

`AddTraxGraphQL` returns the `IServiceCollection`; `UseTraxGraphQL` returns the `WebApplication`.

## Behavior

`UseTraxGraphQL` throws `InvalidOperationException` naming `AddTraxGraphQL()` when the host never
called it.

The schema download (`GET ?sdl`, `/schema`, `/schema.graphql`) and the GraphQL IDE on this endpoint follow the same per-request decision as introspection (see `AllowIntrospection` above): served in Development, 404 elsewhere unless your predicate allows the request. An allowed download is sent `Cache-Control: private`. A host that maps the schema itself with `MapGraphQL(path, "trax")` skips that per-request check; without a predicate, outside Development, the schema's server options switch both off anyway.

`AddTraxGraphQL` registers an `IStartupFilter` that prepends `app.UseWebSockets()` to the pipeline, so the WebSocket transport required for [GraphQL subscriptions](/docs/sdk-reference/graphql-api/subscriptions) is always in place before endpoint execution. You do not need to call `app.UseWebSockets()` yourself, and the ordering of `UseTraxGraphQL()` relative to other endpoint middleware (such as `UseTraxApiDashboard()`) does not affect the upgrade.

The Trax schema accepts a WebSocket upgrade only from origins the host serves, whether `UseTraxGraphQL()` maps it or the host maps it with `MapGraphQL(path, "trax")`: no `Origin` header, the endpoint's own host, or an origin allowed by `AllowSocketOrigins(...)` (default: the origins the CORS default policy names or admits; `AllowAnyOrigin()` admits none). Others get `403`. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#allowed-origins).

### Registered services

`AddTraxGraphQL` calls `AddTraxApi()` (shared API services) and registers:

- **Named GraphQL server** via `AddGraphQLServer("trax")`, using a named schema so it coexists with your own HotChocolate schemas in the same application
- **Query type**: `RootQuery` with grouped sub-types:
  - **`operations`** (`OperationsQueries`): opt-in via `ExposeOperationQueries()`. See [Queries](/docs/sdk-reference/graphql-api/queries).
  - **`discover`** (`DiscoverQueries`): present when trains annotated with [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) are registered, or when entities annotated with [`[TraxQueryModel]`](/docs/sdk-reference/graphql-api/query-models) are discovered via `AddDbContext<T>()`. Contains auto-generated typed query fields for each query train, and paginated/filterable/sortable fields for each query model.
- **Mutation type**: `RootMutation` is registered only when at least one source of mutations exists: a `[TraxMutation]` train, or `ExposeOperationMutations()`. Sub-types:
  - **`operations`** (`OperationsMutations`): opt-in via `ExposeOperationMutations()`. See [Mutations](/docs/sdk-reference/graphql-api/mutations).
  - **`dispatch`** (`DispatchMutations`): only present when trains annotated with [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) are registered. Auto-generated typed mutations with strongly-typed input objects derived from each train's input record. Each train gets a single mutation field (e.g. `banPlayer`) with an optional `mode: ExecutionMode` parameter when both Run and Queue operations are enabled (the default).

- **Subscription type**: `LifecycleSubscriptions`, providing real-time [lifecycle events](/docs/sdk-reference/graphql-api/subscriptions) via WebSocket (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`, `onTrainStateChanged`, `onJunctionEvent`, `onDataChanged`)
- **In-memory subscription transport**: HotChocolate's built-in pub/sub for delivering events to WebSocket clients. Trax's own subscriptions register on their topics one at a time, whichever `ITopicEventReceiver` is registered: HotChocolate's topic can lose one of two subscribes that land together, and the lost subscriber stays connected but never receives an event, which is likeliest when every client reconnects after a restart. A subscription you write yourself against `ITopicEventReceiver` does not go through that gate.
- **Error filter**: `TraxErrorFilter`, which curates exception messages for train-related errors. Exposed types: the message of a plain `TrainException`, which a train author wrote, passes through (`TRAX_TRAIN_ERROR`); a subclass of `TrainException`, yours or Trax's own `TrainAlreadyStartedException`, returns `"The train failed."`, the same answer `operations.workQueue.queueTrain` and `runTrain` give when one refuses a run, so throw a plain `TrainException` for a message the caller should read; the carried message of a `TrainException` a remote worker or nested train reported also passes through; a `TrainException` carrying any other exception type (the `TrainExceptionData` JSON a remote worker's failure comes home as), or wrapping a remote endpoint's reply, returns `"The train failed."` (`TRAX_TRAIN_ERROR`) and the detail stays in the metadata row; `TrainAuthorizationException` always returns the generic `"Not authorized."` (`TRAX_AUTHORIZATION`); `TrainNotFoundException` returns `"The requested train was not found."` (`TRAX_TRAIN_NOT_FOUND`); `AmbiguousTrainNameException` surfaces candidate FullNames (`TRAX_AMBIGUOUS_TRAIN`); `TrainInputValidationException` returns `"The train input failed validation."` (`TRAX_INVALID_INPUT`); the mediator's `NoTrainForInputException`, thrown when no registered train takes the input, returns `"The train could not be run."` (`TRAX_HOST_CONFIGURATION`) even with HotChocolate's exception details switched on, and the full exception, naming the input type and the assemblies the host scanned, is logged on the server at Error, since the fix is in how the host is built. Every mapped error is detached from its exception, so switching on HotChocolate's `IncludeExceptionDetails` adds no `exception` extension (message or stack trace) to any of them. All other exception types (including `InvalidOperationException`) retain HotChocolate's default masked message, and the exception detail when it is switched on. See [Error Codes](/docs/sdk-reference/graphql-api/error-codes).
- **Hardening defaults**: max execution depth of 15, cost-analyzer budget `MaxFieldCost = 1000`, introspection allowed in Development only (decided per request, see `AllowIntrospection`), and a 50-operation per-request cap. Override each via the builder methods above.
- **Subscription auth interceptor**: Trax registers `TraxCompositeSocketInterceptor` so WebSocket subscriptions authenticate from the `connection_init` payload. It serves API-key auth (`authToken`/`apiKey`), JWT auth (`authToken`/`bearer`, including Authority/JWKS schemes) and the JWT dispatcher, together or alone, and accepts every connection when none is registered. Supply your own via `ConfigureSchema(b => b.AddSocketSessionInterceptor<T>())` to replace it. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#authentication).
- **Lifecycle hook**: `GraphQLSubscriptionHook`, automatically registered to publish train state transitions to the subscription transport
- **`GraphQLConfiguration`**, as a singleton: the builder's result. `ModelRegistrations` lists one `QueryModelRegistration` per discovered `[TraxQueryModel]` entity (`EntityType`, `DbContextType`, `Attribute`, `FilterInputType`, `SortInputType`, `AuthorizeAttributes`, `AllowAnonymous`), which a host's own type module can read. `OperationQueriesExposed` and `OperationMutationsExposed` say whether the `operations` namespaces are on, and `MaxExecutionDepth`, `MaxOperationsPerRequest`, `MaxOperationsPerConnection`, `MaxConnectionLifetime` and `ConnectionCredentialRecheckInterval` hold the limits the builder set.

### The operations surface

> The Trax operational surface is **off by default**. The scheduler-control mutations (`triggerManifest`, `cancelGroup`, dead-letter requeue, etc.) call `ITraxScheduler` directly, and an unauthenticated caller could disrupt scheduled work. The read queries (`health`, `manifests`, `executions`, `trains`) reveal deployment topology. Both surfaces have to be opted in explicitly via `ExposeOperationQueries()` and `ExposeOperationMutations()`. Exposing either surface without a gate fails at startup, and there are three ways to answer:

> | Answer | What it does |
> | --- | --- |
> | `GateOperations(policy, roles)` | Gates the `operations` namespace alone. The rest of the endpoint stays open, so public trains and query models keep working. A policy or roles is required; `GateOperationsToAuthenticatedUsers()` is the explicit form for "any signed-in caller". |
> | `RequireAuthorization(policy)` | Gates the whole endpoint, `operations` included. |
> | `AllowAnonymousOperations()` | Acknowledges a deliberately public control plane. |
>
> The read queries carry the same requirement as the mutations. `operations.hosts` returns internal hostnames, environment and per-instance execution counts; `operations.config` returns the scheduler's configuration. That is an infrastructure disclosure whether or not the mutations beside it are gated.

Exposing the operations surface also turns on:

- **Startup guard.** The operations resolvers call `IOperationsService` (and, for the mutations, `ITraxScheduler`). If you expose them without registering those services, the schema still builds and every operation fails at request time with a masked error. To catch this at boot instead, `AddTraxGraphQL()` registers a hosted-service guard that throws `InvalidOperationException` at startup when the surface is exposed but the backing services are missing, including `ITrainExecutionService` (registered by `AddMediator(...)`), which the operations enqueue needs, and, when the mutations are exposed, an `IJobSubmitter`, which [`runTrain`](/docs/sdk-reference/graphql-api/mutations/work-queue#runtrain) hands its run to. A collocated host gets them from `AddScheduler(...)`; an API-only host that talks to a remote scheduler needs `AddTraxJobRunner()` plus `services.AddScoped<IOperationsService, OperationsService>()`, and `AddMediator(...)` registering its trains, because `OperationsService` enqueues `queueTrain` and `requeueExecution` through `ITrainExecutionService`. `AddTraxJobRunner()` registers no job submitter, so an API-only host that exposes the mutations also registers the one its workers read from, `services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()` when they poll PostgreSQL (the scheduler's local workers, or `AddTraxWorker()`); without one the host refuses to start. On such a host a scheduler settings change (`operations.config.updateScheduler`) writes only the fields it names and reaches the running scheduler within seconds, but the first one, which creates the stored settings, must be made on the scheduler host: the API host does not know the scheduler's values for the rest.
- **Stream every train.** Exposing operations marks the host as an administrative/observability surface, so the [lifecycle subscriptions](/docs/sdk-reference/graphql-api/subscriptions) emit events for **all** trains to subscribers that satisfy the operations authorization. Any other subscriber still receives only trains decorated with [`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) whose posture admits it, so a train end users watch keeps `[TraxBroadcast]` on this host too. Without the operations surface, only `[TraxBroadcast]` trains emit (the user-facing default).

If you call `AddTraxGraphQL` with no `[TraxQuery]` trains, no `[TraxQueryModel]` entities, and
`ExposeOperationQueries()` not set, the call throws `InvalidOperationException`, because `RootQuery`
would have no fields and HotChocolate would refuse to build the schema.

### Gating the operations namespace

An anonymous caller or one without the role gets `TRAX_AUTHORIZATION` (`"Not authorized."`) from
any `operations { ... }` field, read or mutation, while `[TraxAllowAnonymous]` surfaces stay
reachable. `queueTrain`, `runTrain` and `requeueExecution` also apply the queued train's own
`[TraxAuthorize]`, so the operator role alone does not let a caller queue a train they may not
run. The [Auth sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Auth) covers each of these end to end.

What refuses startup:

| Configuration | Message begins |
|---|---|
| `ExposeOperationMutations()` with no gate | `ExposeOperationMutations() exposes scheduler-control mutations (...) but the GraphQL endpoint is not gated and the namespace carries no gate of its own, so an unauthenticated caller can reach it. Pick one: GateOperations(policy, roles) ...` |
| `ExposeOperationQueries()` with no gate | `ExposeOperationQueries() exposes the scheduler control plane's read surface (...) but the GraphQL endpoint is not gated ...` |
| `GateOperations()` with no policy and no roles | `GateOperations() needs a policy or roles: GateOperations(policy: "...") or GateOperations(roles: "...").` |
| `GateOperations(...)` without exposing the namespace | `GateOperations() was called but the operations namespace is not exposed, so there is no field to put the gate on` |
| `AllowAnonymousOperations()` with `GateOperations(...)` or `RequireAuthorization()` | `AllowAnonymousOperations() was called together with ...` |
| The namespace exposed without `IOperationsService` (and, with the mutations, `ITraxScheduler` and an `IJobSubmitter`) | names the missing service; `AddScheduler(...)` registers all three |

### Prerequisites

`AddTraxGraphQL` depends on services registered by `AddMediator()` (`ITrainDiscoveryService` and
`ITrainExecutionService`) and a configured data context (`IDataContextProviderFactory`), normally
set up through `AddTrax`. When the `TraxMarker` singleton `AddTrax()` registers is missing, it
throws:

```text
InvalidOperationException: AddTrax() must be called before AddTraxGraphQL().
Call services.AddTrax(...) in your service configuration before calling AddTraxGraphQL().
```

### Registration order

`AddTraxGraphQL()` needs `AddTrax()` to have run first, and every `[TraxQuery]`, `[TraxMutation]`
or `[TraxBroadcast]` train registered before it. A train registered afterwards refuses the host at
startup, naming the train. Authentication may come before or after it: the subscription
interceptor reads the registered schemes once the container is complete.

The startup checks `AddTraxGraphQL()` registers (an exposed surface missing its services, a posture
contradiction, a train registered after it) run in the hosted-service `StartingAsync` step, which
the host finishes for every service before it starts any. A refused host never binds Kestrel or
starts a worker, even with `HostOptions.ServicesStartConcurrently`.

Everything else is order-independent too. `@authorize` is attached to the schema, so query and
mutation gating works whichever way round the host is composed, and services the GraphQL
components depend on are resolved on first use rather than when `AddTraxGraphQL()` runs.

## Examples

A host serving the endpoint at the default path:

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddTrax(trax => trax
        .AddEffects(effects => effects
            .UsePostgres(builder.Configuration.GetConnectionString("TraxDatabase")!)
        )
        .AddMediator(typeof(Program).Assembly)
    )
    .AddTraxGraphQL();

var app = builder.Build();

app.UseTraxGraphQL(); // serves at /trax/graphql

app.Run();
```

The builder overload:

```csharp compile
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

A different path:

```csharp compile
app.UseTraxGraphQL(routePrefix: "/api/graphql");
```

Endpoint authorization, which gates everything on the route, the IDE page included:

```csharp compile
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization("AdminPolicy"));
```

Execution authorization, which gates every operation while the IDE page follows `AllowIntrospection`:

```csharp compile
builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
```

The two are independent.

A host that serves public trains or query models and an operations namespace for operators:

```csharp compile
using Trax.Api.GraphQL.Extensions;

builder.Services.AddTraxGraphQL(graphql => graphql
    .ExposeOperationQueries()
    .ExposeOperationMutations()
    .GateOperations(roles: "Operator"));
```

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries), [Mutations](/docs/sdk-reference/graphql-api/mutations), [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions)
- [Query models](/docs/sdk-reference/graphql-api/query-models)
- [Configure filtering](/docs/sdk-reference/graphql-api/configure-filtering)
- [Authorization](/docs/authorization)
- [API security](/docs/api-security)
