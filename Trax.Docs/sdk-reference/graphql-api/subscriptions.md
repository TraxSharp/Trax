---
layout: default
title: Subscriptions
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 5
---

# Subscriptions

Trax provides real-time GraphQL subscriptions over WebSocket. There are two kinds: per-train lifecycle events (started, completed, failed, cancelled) and a coalesced `onDataChanged` signal that tells an admin UI which data domain changed so it can refetch without polling.

Subscriptions are powered by HotChocolate's built-in subscription infrastructure with an in-memory pub/sub transport. They are automatically enabled when you call `AddTraxGraphQL()`.

**Which trains emit lifecycle events depends on what the host exposes:**

- **User-facing host** (subscriptions but no operations surface): only trains decorated with [`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) emit. This is the opt-in for streaming a curated subset of trains to your app's own clients; others are silently skipped.
- **Admin host** (calls `ExposeOperationQueries()` / `ExposeOperationMutations()`): **every** train emits, regardless of `[TraxBroadcast]`. An operations dashboard should observe all server activity, so exposing the operations surface flips the lifecycle subscriptions to stream everything. You do not decorate trains for the admin dashboard to see them.

Data-change signals (`onDataChanged`) are unrelated to `[TraxBroadcast]` and fire for the scheduler/admin domains regardless.

## Lifecycle Subscription Fields

The lifecycle subscriptions return a `TrainLifecycleEvent` payload.

| Field | Description |
|-------|-------------|
| `onTrainStarted` | Fires when a train begins execution |
| `onTrainCompleted` | Fires when a train completes successfully |
| `onTrainFailed` | Fires when a train fails with an exception |
| `onTrainCancelled` | Fires when a train is cancelled via `CancellationToken` |
| `onTrainStateChanged` | Fires on every lifecycle transition (one field drives a whole live feed) |

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

## Data Change Signals

`onDataChanged` is a single subscription that fires when a scheduler/admin data domain changes. It carries only which domain changed, never the changed rows, so a client uses it as a nudge to refetch its own bounded, paged view. This is how the dashboard's list pages update live without a poll timer.

```graphql
type DataChangedEvent {
  domain: ChangeDomain!
  timestamp: DateTime!
}

enum ChangeDomain {
  WORK_QUEUE
  DEAD_LETTER
  MANIFEST
  MANIFEST_GROUP
  SCHEDULER_CONFIG
}
```

| Domain | Fires when |
|--------|------------|
| `WORK_QUEUE` | Entries are queued, dispatched, or cancelled |
| `DEAD_LETTER` | A dead letter is created (retries exhausted), requeued, or acknowledged |
| `MANIFEST` | A manifest is edited, enabled, or disabled (not on routine schedule recompute) |
| `MANIFEST_GROUP` | A manifest group's configuration changes |
| `SCHEDULER_CONFIG` | The scheduler configuration changes |

```graphql
subscription {
  onDataChanged {
    domain
    timestamp
  }
}
```

Signals are **coalesced**: a burst of writes to one domain (for example a dispatch cycle touching thousands of work-queue rows) collapses into a single `onDataChanged` event per short window, so a subscriber refetches at most once per window instead of once per row. Filter by `domain` on the client to refetch just the affected view.

### Emitting signals

Write paths emit signals through `ITraxChangeSignal`, a singleton registered by `AddTrax()`:

```csharp
public class MyService(ITraxChangeSignal changeSignal)
{
    public async Task DoWorkAsync(IDataContext db, CancellationToken ct)
    {
        // ... mutate and persist ...
        await db.SaveChanges(ct);
        changeSignal.Notify(ChangeDomain.WorkQueue); // fire-and-forget after the commit
    }
}
```

`Notify` never throws and never blocks the caller; under sustained pressure signals are dropped rather than queued unbounded (a coalesced refetch is coming regardless). A background coalescer flushes the distinct set of changed domains to the `onDataChanged` topic. Trax's own scheduler and GraphQL write paths already call `Notify`, so the dashboard gets live updates out of the box; call it yourself only from custom write paths that should nudge a dashboard view.

## WebSocket Connection

Subscriptions use the GraphQL over WebSocket protocol. Connect to the same endpoint as queries and mutations:

```
ws://localhost:5000/trax/graphql
```

In Banana Cake Pop (the built-in GraphQL IDE), subscriptions work out of the box. Just write a subscription query and execute it.

For programmatic clients, use any GraphQL client that supports the `graphql-ws` protocol (e.g., Apollo Client, urql, Strawberry Shake).

`AddTraxGraphQL()` wires the WebSocket upgrade middleware at the front of the pipeline (via an `IStartupFilter`), so the handshake upgrades no matter where you place `UseTraxGraphQL()` relative to other endpoint middleware such as `UseTraxDashboard()`. You do not need to call `app.UseWebSockets()` yourself.

### Reconnection

The server does not persist per-subscriber state, and there is no replay: events emitted while a client's socket is down are not redelivered. A resilient client should therefore do two things, both of which the Trax dashboard does:

- **Reconnect indefinitely on transient failures.** `graphql-ws` gives up after 5 attempts by default; set `retryAttempts: Infinity` so the socket survives server restarts and network drops. Active subscriptions are re-established automatically on each reconnect, and because the credential rides in `connection_init`, each reconnect re-authenticates. Do *not* retry on auth-rejection close codes (4400/4401/4403/4429): a retry can't fix a bad credential, so re-auth by terminating and reopening the socket instead.
- **Refetch on recovery.** Because missed events are gone, refetch the visible data once when the socket transitions back to connected. Pairing this with the coalesced `onDataChanged` signal keeps grids correct: the signal drives incremental refetches while connected, and the reconnect refetch fills the gap for anything that changed while it wasn't.

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

### Register authentication before AddTraxGraphQL

The interceptor that reads the credential is chosen from what is registered when
`AddTraxGraphQL()` runs, so the auth call has to come first:

```csharp
builder.Services.AddTraxJwtAuth(...);   // must precede AddTraxGraphQL
builder.Services.AddTraxGraphQL(...);
```

The other order fails at startup with a message naming the call to move. Before that check
existed it started fine and accepted every connection anonymously, because HotChocolate falls
back to an interceptor that accepts everything and HTTP gating is unaffected.

## Architecture

Subscriptions are powered by the [lifecycle hooks](/docs/sdk-reference/configuration/add-lifecycle-hook) system. The `GraphQLSubscriptionHook` is automatically registered by `AddTraxGraphQL()` and publishes lifecycle events to HotChocolate's in-memory subscription transport.

At startup, the hook builds a set of canonical train names (using `ServiceType.FullName`, the interface name) from registrations that have `[TraxBroadcast]`. On each lifecycle event it publishes when the train is opted in. **If the operations surface is exposed, it publishes for every train** (`TrainLifecycleStreamOptions.StreamAllTrains`, set automatically by `AddTraxGraphQL()` when `ExposeOperationQueries()`/`ExposeOperationMutations()` is called).

```
ServiceTrain.Run()
  → LifecycleHookRunner.OnCompleted()
    → GraphQLSubscriptionHook.OnCompleted()
      → Publish if: operations surface exposed (all trains), OR
                    metadata.Name matches a [TraxBroadcast] train's ServiceType.FullName
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

When a broadcaster is configured, `AddTraxGraphQL()` automatically registers a `GraphQLTrainEventHandler` that receives remote lifecycle events from the message bus and forwards them to HotChocolate's subscription transport. It applies the same rule as the local `GraphQLSubscriptionHook`: forward every train when the operations surface is exposed, otherwise only `[TraxBroadcast]` trains (matched by canonical `ServiceType.FullName`), regardless of which process executes them. Events from the local process are de-duplicated automatically.

Data-change signals ride the same bridge. In a single-process deployment (the API collocated with the scheduler), `onDataChanged` works with no broadcaster: signals flow in-process to the subscription topic. When the scheduler runs in a separate process, `UseBroadcaster()` forwards its `Notify` calls over the message bus (a `BroadcastChangeSink`), and the API's `GraphQLDataChangeHandler` re-publishes them to local subscribers. The originating process ignores its own broadcast via the same executor de-duplication used for lifecycle events.

See [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) for full details.

## Package

```
dotnet add package Trax.Api.GraphQL
```
