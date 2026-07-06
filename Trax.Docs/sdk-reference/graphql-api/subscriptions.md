---
layout: default
title: Subscriptions
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 5
---

# Subscriptions

Trax provides real-time GraphQL subscriptions for train lifecycle events. Clients connect via WebSocket and receive events as trains transition through states (started, completed, failed, cancelled).

Subscriptions are powered by HotChocolate's built-in subscription infrastructure with an in-memory pub/sub transport. They are automatically enabled when you call `AddTraxGraphQL()`.

**Only trains decorated with [`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) emit subscription events.** Trains without the attribute are silently skipped.

## Subscription Fields

All subscriptions return a `TrainLifecycleEvent` payload.

| Field | Description |
|-------|-------------|
| `onTrainStarted` | Fires when a train begins execution |
| `onTrainCompleted` | Fires when a train completes successfully |
| `onTrainFailed` | Fires when a train fails with an exception |
| `onTrainCancelled` | Fires when a train is cancelled via `CancellationToken` |

## TrainLifecycleEvent Payload

```graphql
type TrainLifecycleEvent {
  metadataId: Long!
  externalId: String!
  trainName: String!
  trainState: TrainState!
  timestamp: DateTime!
  failureJunction: String
  failureReason: String
}
```

| Field | Description |
|-------|-------------|
| `metadataId` | The database metadata row ID for this execution |
| `externalId` | The external identifier assigned to this execution |
| `trainName` | The canonical train name (the service interface's fully-qualified name, e.g. `MyApp.Trains.IProcessOrderTrain`) |
| `trainState` | The current state of the train (`InProgress`, `Completed`, `Failed`, `Cancelled`) |
| `timestamp` | When the event occurred (end time if available, otherwise current UTC time) |
| `failureJunction` | The junction that failed (only present on failed trains) |
| `failureReason` | The failure message (only present on failed trains) |

## Examples

### Subscribe to all completed trains

```graphql
subscription {
  onTrainCompleted {
    metadataId
    trainName
    trainState
    timestamp
  }
}
```

### Subscribe to failures

```graphql
subscription {
  onTrainFailed {
    metadataId
    trainName
    failureJunction
    failureReason
    timestamp
  }
}
```

### Subscribe to all lifecycle events

Open multiple subscriptions in parallel:

```graphql
# Tab 1
subscription { onTrainStarted { metadataId trainName trainState } }

# Tab 2
subscription { onTrainCompleted { metadataId trainName trainState } }

# Tab 3
subscription { onTrainFailed { metadataId trainName failureJunction failureReason } }

# Tab 4
subscription { onTrainCancelled { metadataId trainName trainState } }
```

## WebSocket Connection

Subscriptions use the GraphQL over WebSocket protocol. Connect to the same endpoint as queries and mutations:

```
ws://localhost:5000/trax/graphql
```

In Banana Cake Pop (the built-in GraphQL IDE), subscriptions work out of the box. Just write a subscription query and execute it.

For programmatic clients, use any GraphQL client that supports the `graphql-ws` protocol (e.g., Apollo Client, urql, Strawberry Shake).

`AddTraxGraphQL()` wires the WebSocket upgrade middleware at the front of the pipeline (via an `IStartupFilter`), so the handshake upgrades no matter where you place `UseTraxGraphQL()` relative to other endpoint middleware such as `UseTraxDashboard()`. You do not need to call `app.UseWebSockets()` yourself.

## Authentication

Browsers cannot attach an `Authorization` header to a WebSocket upgrade, so the credential travels in the `connection_init` payload instead:

```json
{ "type": "connection_init", "payload": { "authToken": "..." } }
```

Trax authenticates that payload with a HotChocolate `ISocketSessionInterceptor`. Which interceptor is wired depends on the auth you registered.

### Stock interceptors

| Auth registered | Interceptor | Payload keys |
|---|---|---|
| `AddTraxApiKeyAuth` | `TraxApiKeySocketInterceptor` | `authToken` or `apiKey` |
| `AddTraxJwtAuth` | `TraxJwtSocketInterceptor` | `authToken` or `bearer` |

Both are wired automatically, but only when the matching principal resolver is already in the service collection at the time `AddTraxGraphQL()` runs. Register your `AddTrax*Auth` call **before** `AddTraxGraphQL()`, which matches the standard `AddTrax(...).AddTraxGraphQL(...)` ordering.

The JWT interceptor validates against the same `JwtBearerOptions` as the HTTP handler, including Authority/JWKS schemes (Cognito, Google, any OIDC provider): it fetches signing keys from the scheme's discovery document when the options carry no static key.

Cookie auth (`Trax.Api.Auth.Oidc`) needs no interceptor. The browser sends cookies on the upgrade request and the cookie scheme authenticates it like any HTTP request.

HotChocolate runs a single interceptor per schema. When both the API-key and JWT interceptors are wired, the last one registered wins (JWT), so a connection presenting an API-key token while both are active is rejected. Use one credential type on subscriptions, or supply a custom interceptor (below).

### Multiple JWT issuers

[`AddTraxJwtDispatcher`](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher) routes subscription tokens by their `iss` claim across every mapped scheme, the same way it routes HTTP requests. When a dispatcher is registered, Trax wires `TraxJwtDispatcherSocketInterceptor` in place of the single-scheme JWT interceptor. Each scheme validates fully (signature, issuer, audience, lifetime, JWKS), so an unmapped or forged issuer is rejected.

```csharp
services.AddTraxJwtAuth("cognito", jwt => jwt.UseAuthority(cognitoAuthority, "mobile-client"));
services.AddTraxJwtAuth("internal", jwt => jwt.UseSymmetricKey("nwyc-web", "trax", internalKey));
services.AddTraxJwtDispatcher(d => d
    .MapIssuer(cognitoAuthority, "cognito")
    .MapIssuer("nwyc-web", "internal"));
```

### Custom interceptor

To replace the stock interceptors (for example, to authenticate against a scheme Trax does not model), supply your own through `ConfigureSchema`:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<AppDbContext>()
    .ConfigureSchema(b => b.AddSocketSessionInterceptor<MySocketInterceptor>()));
```

This registration overrides the stock interceptors and is independent of when auth was registered in the service collection. The interceptor's own dependencies resolve per connection, so it only needs them in DI by app start. Derive from `DefaultSocketSessionInterceptor`, read the credential from the `connection_init` payload in `OnConnectAsync`, and return `ConnectionStatus.Reject(...)` to refuse the connection or attach the principal to `session.Connection.HttpContext.User` and call `base.OnConnectAsync(...)` to accept.

## Architecture

Subscriptions are powered by the [lifecycle hooks](/docs/sdk-reference/configuration/add-lifecycle-hook) system. The `GraphQLSubscriptionHook` is automatically registered by `AddTraxGraphQL()` and publishes lifecycle events to HotChocolate's in-memory subscription transport.

At startup, the hook builds a set of canonical train names (using `ServiceType.FullName`, the interface name) from registrations that have `[TraxBroadcast]`. On each lifecycle event, it checks the train's metadata name against this set and only publishes if the train is opted in.

```
ServiceTrain.Run()
  → LifecycleHookRunner.OnCompleted()
    → GraphQLSubscriptionHook.OnCompleted()
      → Check: does metadata.Name match a [TraxBroadcast] train's ServiceType.FullName?
        → Yes → ITopicEventSender.SendAsync("OnTrainCompleted", event)
          → WebSocket clients receive the event
        → No → skip (no event published)
```

## Cross-Process Subscriptions

By default, subscriptions only fire for trains that execute in the same process as the GraphQL API. In distributed deployments where trains are queued and executed by separate worker processes, use [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster) to bridge the gap:

```csharp
// Both hub and worker:
effects.UseBroadcaster(b => b.UseRabbitMq("amqp://guest:guest@localhost:5672"))
```

When a broadcaster is configured, `AddTraxGraphQL()` automatically registers a `GraphQLTrainEventHandler` that receives remote lifecycle events from the message bus and forwards them to HotChocolate's subscription transport. Like the local `GraphQLSubscriptionHook`, the remote handler filters by `[TraxBroadcast]` using canonical train names (`ServiceType.FullName`). Only trains with the attribute produce subscription events, regardless of which process executes them. Events from the local process are de-duplicated automatically.

See [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) for full details.

## Package

```
dotnet add package Trax.Api.GraphQL
```
