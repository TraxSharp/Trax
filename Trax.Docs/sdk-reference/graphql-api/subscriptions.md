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

## Who receives what

Each subscription carries the authorization of the data it streams, decided for each subscriber when it subscribes:

- **Operations view.** When the operations surface is exposed, a subscriber that satisfies the operations authorization receives every train, with the same detail `operations.executions` shows. That is the `GateOperations(...)` gate, or no further check when the host chose `AllowAnonymousOperations()` or gated the whole endpoint with `RequireAuthorization(...)`.
- **Broadcast view.** Any other subscriber receives only `[TraxBroadcast]` trains whose own posture admits them: `[TraxAllowAnonymous]` admits everyone, and `[TraxAuthorize]` an authenticated caller meeting its policies and roles. For these subscribers `failureReason` is shown only when the train failed with a `TrainException` (whose message is written for clients); otherwise it reads `Unexpected Execution Error`. `hostName` and `hostEnvironment` are withheld.
- A subscriber who could receive nothing is refused with `TRAX_AUTHORIZATION` when it subscribes.
- `onDataChanged` needs the operations authorization when the operations surface is exposed, and an authenticated caller when it is not.

On an open endpoint a `[TraxBroadcast]` train must declare `[TraxAuthorize]` or `[TraxAllowAnonymous]`, or the host does not start. The `output` field carries the train's output as JSON, objects and arrays included.

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
  hostName: String
  hostEnvironment: String
  output: Any
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
| `failureReason` | The failure message (only present on failed trains; masked outside the operations view unless the train raised a `TrainException`) |
| `hostName` / `hostEnvironment` | The host that ran the train (operations view only) |
| `output` | The train's output as JSON |

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
  EXECUTION
}
```

| Domain | Fires when |
|--------|------------|
| `WORK_QUEUE` | Entries are queued, dispatched, or cancelled |
| `DEAD_LETTER` | A dead letter is created (retries exhausted), requeued, or acknowledged |
| `MANIFEST` | A manifest is edited, enabled, or disabled (not on routine schedule recompute) |
| `MANIFEST_GROUP` | A manifest group's configuration changes |
| `SCHEDULER_CONFIG` | The scheduler configuration changes |
| `EXECUTION` | Cancellation is requested for one or more runs, so a runs view should refetch. The value exists from Trax.Effect 1.56.0 and is emitted by a Trax.Scheduler that signals it; a receiver on an older version drops it |

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

### Allowed origins

A WebSocket upgrade that carries an `Origin` header is accepted only from origins the host serves: the endpoint's own host, or an allowed origin. Anything else is answered with `403` before the handshake. An upgrade with no `Origin` header, which is what non-browser clients send, is accepted. The allowed origins default to those of your CORS default policy (`AddCors(o => o.AddDefaultPolicy(...))`, including `AllowAnyOrigin()`); set them explicitly with `AllowSocketOrigins(...)` on the GraphQL builder, which replaces the CORS default:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<AppDbContext>()
    .AllowSocketOrigins("https://app.example.com", "https://admin.example.com"));
```

The endpoint's own host is compared without its scheme, so an https page served through a TLS-terminating proxy is recognised. The check applies wherever the Trax schema serves a socket, whether `UseTraxGraphQL()` maps it or you map it yourself with `MapGraphQL(path, "trax")`, and it does not apply to another HotChocolate schema on the same host. A host whose browser clients are on another origin and whose CORS policy is a named one, not the default, needs `AllowSocketOrigins(...)`.

### Reconnection

The server does not persist per-subscriber state, and there is no replay: events emitted while a client's socket is down are not redelivered. A resilient client should therefore do two things, both of which the Trax dashboard does:

- **Reconnect indefinitely on transient failures.** `graphql-ws` gives up after 5 attempts by default; set `retryAttempts: Infinity` so the socket survives server restarts and network drops. Active subscriptions are re-established automatically on each reconnect, and because the credential rides in `connection_init`, each reconnect re-authenticates. Do *not* retry on auth-rejection close codes (4400/4401/4403/4429): a retry can't fix a bad credential, so re-auth by terminating and reopening the socket instead.
- **Refetch on recovery.** Because missed events are gone, refetch the visible data once when the socket transitions back to connected. Pairing this with the coalesced `onDataChanged` signal keeps grids correct: the signal drives incremental refetches while connected, and the reconnect refetch fills the gap for anything that changed while it wasn't.

## Authentication

Browsers cannot attach an `Authorization` header to a WebSocket upgrade, so the credential travels in the `connection_init` payload instead:

```json
{ "type": "connection_init", "payload": { "authToken": "..." } }
```

Trax authenticates that payload with one HotChocolate `ISocketSessionInterceptor`, `TraxCompositeSocketInterceptor`, which `AddTraxGraphQL()` registers for every host. It hands each connection to the strategy for the credential it carries.

### Which strategy handles a connection

| Auth registered | Strategy | Payload keys |
|---|---|---|
| `AddTraxApiKeyAuth` | `TraxApiKeySocketInterceptor` | `authToken` or `apiKey` |
| `AddTraxJwtAuth` (default or named schemes) | each registered scheme in turn; the first that validates the token resolves the principal | `authToken` or `bearer` |
| `AddTraxJwtDispatcher` | `TraxJwtDispatcherSocketInterceptor`, in place of the single-scheme JWT one | `authToken` or `bearer` |
| none of these | none: every connection is accepted | |

With one scheme registered, every connection goes to it. With API-key and JWT auth both registered, the payload decides:

- An `authToken` whose header parses as a JWT is validated as a JWT. Any other `authToken` is looked up as an API key.
- Without an `authToken`, `apiKey` is looked up as an API key and `bearer` is validated as a JWT.
- A payload with none of the three is rejected.

A credential is checked by one strategy only. A JWT that fails validation is rejected, not retried as an API key.

The schemes are read from the finished container on the first connection, so it does not matter whether your `AddTrax*Auth` call comes before or after `AddTraxGraphQL()`.

A JWT in `connection_init` is authenticated by the scheme's own `JwtBearerHandler`, exactly as an HTTP request carrying it as `Authorization: Bearer` would be. The handler runs against a request built from the upgrade request (its path, query, headers and addresses), so everything that applies over HTTP applies on the socket:

- signature, issuer, audience, lifetime and clock skew, from the scheme's `JwtBearerOptions`;
- Authority/JWKS schemes (Cognito, Google, any OIDC provider), including the refresh of the JWKS when a token is signed with a key id the cached document does not have, so a rotated key is picked up;
- your `JwtBearerEvents`, set through `CustomizeBearerOptions`: `OnMessageReceived`, `OnTokenValidated` (a revocation or tenant check that calls `context.Fail(...)` refuses the socket), `OnAuthenticationFailed`;
- the claim mapping the handler applies (`sub` arrives as `ClaimTypes.NameIdentifier` unless `MapInboundClaims` is off), and any `IClaimsTransformation`.

A refused connection is told only `Invalid JWT.`; the handler's reason is logged, not sent, as HTTP returns only a 401.

**A connection is closed when its token expires.** A socket outlives the moment its token was checked, so at the token's `exp` the server closes it with close code 1008 (policy violation) and the message `The access token has expired.`. An HTTP request with that token would be refused from then on. The client reconnects with a fresh token, which `connection_init` carries on every reconnect. Revoking a user or a key does not close a socket that is already open; the token's lifetime bounds how long it stays open, so keep access tokens short-lived.

Each connection gets its own DI scope. The interceptor itself is a singleton (HotChocolate builds one per schema), so it opens a scope when `connection_init` arrives, runs the handler and your scoped `ITraxPrincipalResolver<T>` inside it, and disposes it once the principal is resolved. A resolver holding a `DbContext` works on subscriptions exactly as it does on HTTP. The principal is captured onto the connection's `HttpContext.User` and reused for every subsequent operation until the connection closes.

Cookie auth (`Trax.Api.Auth.Oidc`) needs no interceptor. The browser sends cookies on the upgrade request and the cookie scheme authenticates it like any HTTP request.

That holds only when no token scheme is registered. Once `AddTraxApiKeyAuth`, `AddTraxJwtAuth` or `AddTraxJwtDispatcher` is registered, every connection needs a credential in `connection_init`, and an upgrade that is already authenticated by a session cookie is rejected without one. A browser app on a host with both cookie and token auth sends its token in the payload.

### Multiple JWT issuers

[`AddTraxJwtDispatcher`](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher) routes subscription tokens by their `iss` claim across every mapped scheme, the same way it routes HTTP requests. When a dispatcher is registered, JWT connections go to `TraxJwtDispatcherSocketInterceptor` instead of the single-scheme JWT strategy. The matched scheme's handler then authenticates the token in full, events and JWKS refresh included, so an unmapped or forged issuer is rejected.

```csharp
services.AddTraxJwtAuth("cognito", jwt => jwt.UseAuthority(cognitoAuthority, "mobile-client"));
services.AddTraxJwtAuth("internal", jwt => jwt.UseSymmetricKey("nwyc-web", "trax", internalKey));
services.AddTraxJwtDispatcher(d => d
    .MapIssuer(cognitoAuthority, "cognito")
    .MapIssuer("nwyc-web", "internal"));
```

### Custom interceptor

To replace Trax's interceptor (for example, to authenticate against a scheme Trax does not model), supply your own through `ConfigureSchema`:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<AppDbContext>()
    .ConfigureSchema(b => b.AddSocketSessionInterceptor<MySocketInterceptor>()));
```

This registration replaces `TraxCompositeSocketInterceptor` and is independent of when auth was registered in the service collection. The interceptor's own dependencies resolve per connection, so it only needs them in DI by app start. Derive from `DefaultSocketSessionInterceptor`, read the credential from the `connection_init` payload in `OnConnectAsync`, and return `ConnectionStatus.Reject(...)` to refuse the connection or attach the principal to `session.Connection.HttpContext.User` and call `base.OnConnectAsync(...)` to accept.

### The endpoint policy

When the GraphQL builder calls `RequireAuthorization(policy)`, the policy applies to the socket as it does to HTTP: the connection's principal must satisfy it at `connection_init`, and every query, mutation and subscription the socket carries is checked again before it runs. A connection with no authenticated principal is refused.

### Registration order

Subscription auth does not depend on it. Earlier versions chose the interceptor from what was registered when `AddTraxGraphQL()` ran, so an auth call placed after it left subscriptions accepting every connection while HTTP stayed gated, and a later version refused to start instead. The composite reads the registered schemes once the container is complete, so either order authenticates.

If a token scheme is registered and HotChocolate's own accept-everything interceptor is the active one, the host refuses to start. Only registering `DefaultSocketSessionInterceptor` yourself produces that.

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
