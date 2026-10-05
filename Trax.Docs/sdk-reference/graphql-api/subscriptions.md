---
layout: default
title: Subscriptions
description: "Reference for Trax GraphQL subscriptions: train lifecycle events, a run's junction events, the onDataChanged signal, payloads, WebSocket and authentication."
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 5
---

# Subscriptions

Trax provides real-time GraphQL subscriptions over WebSocket. There are three kinds: per-train lifecycle events (started, completed, failed, cancelled), the steps of one run ([`onJunctionEvent`](#onjunctionevent)), and a coalesced `onDataChanged` signal that tells an admin UI which data domain changed so it can refetch without polling.

Subscriptions are powered by HotChocolate's built-in subscription infrastructure with an in-memory pub/sub transport. They are automatically enabled when you call `AddTraxGraphQL()`.

**Which trains emit lifecycle events depends on what the host exposes:**

- **User-facing host** (subscriptions but no operations surface): only trains decorated with [`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) emit. This is the opt-in for streaming a curated subset of trains to your app's own clients; others are silently skipped.
- **Admin host** (calls `ExposeOperationQueries()` / `ExposeOperationMutations()`): **every** train emits, but only to subscribers that satisfy the operations authorization. An operations dashboard should observe all server activity, so you do not decorate trains for the admin dashboard to see them. Every other subscriber on that host still receives only `[TraxBroadcast]` trains whose posture admits it, and one that no train admits is refused with `Not authorized.` (`TRAX_AUTHORIZATION`). A train your app's own clients watch carries `[TraxBroadcast]` on an admin host too.

Data-change signals (`onDataChanged`) are unrelated to `[TraxBroadcast]` and fire for the scheduler/admin domains regardless.

## Who receives what

Each subscription carries the authorization of the data it streams, decided for each subscriber when it subscribes:

- **Operations view.** When the operations surface is exposed, a subscriber that satisfies the operations authorization receives every train, with the same detail `operations.executions` shows. That is the `GateOperations(...)` gate, or no further check when the host chose `AllowAnonymousOperations()` or gated the whole endpoint with `RequireAuthorization(...)`.
- **Broadcast view.** Any other subscriber receives only `[TraxBroadcast]` trains whose own posture admits them: `[TraxAllowAnonymous]` admits everyone who can open a socket (on a host with an API-key or JWT scheme, only a connection that brings a credential; see [Authentication](#authentication)), and `[TraxAuthorize]` an authenticated caller meeting its policies and roles. For these subscribers `failureReason` is shown only when the train failed with a `TrainException` (whose message is written for clients); otherwise it reads `Unexpected Execution Error`. This holds for a train that ran on another node and reached this one over [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster): the message carries the exception type, so the reason is shown or masked exactly as for a local run. A message from a publisher older than Trax.Effect 1.57.4 has no exception type, and its reason is masked. `hostName` and `hostEnvironment` are withheld.
- A subscriber who could receive nothing is refused with `TRAX_AUTHORIZATION` when it subscribes: the subscription gets a `graphql-transport-ws` message `{"id":"1","type":"error","payload":[{"message":"Not authorized.","extensions":{"code":"TRAX_AUTHORIZATION"}}]}` and the socket stays open. On a host that exposes operations, a non-operator subscriber is refused this way when none of the trains it is allowed to watch carries `[TraxBroadcast]`.
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
  sequence: Long!
  output: Any
}
```

| Field | Description |
|-------|-------------|
| `metadataId` | The database metadata row ID for this execution |
| `externalId` | The external identifier assigned to this execution |
| `trainName` | The canonical train name (the service interface's fully-qualified name, e.g. `MyApp.Trains.IProcessOrderTrain`) |
| `trainState` | The current state of the train, as the GraphQL enum values `IN_PROGRESS`, `COMPLETED`, `FAILED` or `CANCELLED` (C# `TrainState.InProgress` and so on) |
| `timestamp` | When the event occurred (end time if available, otherwise current UTC time) |
| `failureJunction` | The junction that failed (only present on failed trains) |
| `failureReason` | The failure message (only present on failed trains; masked outside the operations view unless the train raised a `TrainException`) |
| `hostName` / `hostEnvironment` | The host that ran the train (operations view only) |
| `output` | The train's output as JSON |
| `sequence` | This event's position in the subscription: 1 for the first event, one more for each after it, and a skipped number after events were lost. See [Lost events](#lost-events) |

## Lost events

The lifecycle subscriptions are a live feed, not a log. HotChocolate keeps a bounded buffer for each subscriber (64 events), and when a subscriber falls behind, for example a slow socket while many trains change state at once, the oldest buffered events are dropped. The newest event always arrives.

Every lifecycle event carries `sequence`, numbered per subscription. It counts up by one, and after a loss it skips one number, whatever was lost:

```text
sequence: 1, 2, 3, 5, 6   # something was lost between 3 and 5
```

A client that sees a number other than the previous one plus one has missed state changes, and should refetch what it shows (for an admin view, `operations.executions`; otherwise its own queries) and carry on reading the feed. The skip is always one number, so a subscriber outside the operations view does not learn how much activity there was in trains it cannot see. A loss of an event the subscriber would not have received is reported too, since the subscription cannot tell whose event was dropped; the refetch then changes nothing.

Events a host sends through HotChocolate's `ITopicEventSender` itself, rather than through Trax's hooks, are not numbered and never cause a skip. Numbers are per API node. `Trax.Api/docs/adr/0031` records the decision.

## onJunctionEvent

```graphql
subscription {
  onJunctionEvent(metadataId: 100) {
    eventType
    timestamp
    sequence
    junction {
      position
      kind
      name
      state
      durationMs
      failureClass
      failureException
      questionKey
      answer
      confidence
      replayed
      answerWithheld
      nameWithheld
      trackPosition
      attempt
    }
  }
}
```

Fires for each step of the run `metadataId`: a junction starting, completing, failing or being
cancelled, a question a routing step asked, and the track it took. Only a host that calls
[`AddJunctionEvents()`](/docs/sdk-reference/configuration/add-junction-events) publishes them. The
run is a required argument; there is no feed of every run's steps.

You receive a run's steps exactly when you would receive that run's train events: a train's steps
are published by the same rule as its events (`[TraxBroadcast]`, or every train when the operations
surface is exposed), and each subscriber's view is the one [Who receives what](#who-receives-what)
describes, including the refusal of a subscriber who could receive nothing. Outside the operations
view a step loses host detail, as a train event does: `decider` is null, and `failureException` is
shown only for a `TrainException`. A broadcast subscriber also gets `answer` and `confidence` as
null, unless the host calls
[`AllowJunctionAnswersForBroadcastSubscribers()`](/docs/sdk-reference/graphql-api/add-trax-graphql#builder-methods).
Without that call, every step with a `trackPosition` (any step after a routing step, of any kind: a
junction, a `Choice`, `Score` or `YesNo` question, or a further route) arrives named `(withheld)`
with `nameWithheld` true, and with `questionKey`, `answer` and `confidence` null, because each of
them can say which track ran. The operations view always sees answers and names.

A withheld step still arrives with its kind, position, state, timing and failure class, so the
shape of a run stays visible: where a routing step's tracks run a different number of steps, or
take different times, that shape can tell them apart. A train whose track shape is as sensitive as
its answer belongs off broadcast, with its runs followed through the operations view.
`Trax.Api/docs/adr/0037` records why.

| Field | Description |
|-------|-------------|
| `metadataId`, `externalId`, `trainName`, `timestamp` | As on `TrainLifecycleEvent` |
| `eventType` | `JUNCTION_STARTED`, `JUNCTION_COMPLETED`, `JUNCTION_FAILED`, `JUNCTION_CANCELLED`, `DECIDED`, `DECISION_REFUSED` or `ROUTED` |
| `junction` | The step, a `JunctionStep`: `position`, `kind`, `name`, `state`, `startedAt`, `endedAt`, `durationMs`, `failureClass`, `failureException`, `questionKey`, `answer`, `confidence`, `replayed`, `decider`, `answerWithheld`, `nameWithheld: Boolean!`, `trackPosition: Int`, `attempt` |
| `sequence` | Numbered as lifecycle events are. See [Lost events](#lost-events) |

A step never carries the train's input or output or a failure's message. An answer to a question
about a [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) type
is null, with `answerWithheld` true, in every view. After a routing step whose answer is withheld
this way, every later step of the run is withheld in every view too: named `(withheld)`, with
`nameWithheld` true, and a question or route has its `questionKey`, `answer` and `confidence` null.
What such a step still shows is the shape described above; the run's `failureJunction` on its
[failed event](#trainlifecycleevent-payload), and a train started from a junction on the track, are
reported as for any run.

A `metadataId` of 0 or less is refused with `TRAX_INVALID_ARGUMENT`. Publishing a step to the
subscription waits at most 250 ms on the run's path; a step that cannot be published in time is
dropped and shows as a skip in `sequence`. A send still running when the bound expires or the run is
cancelled keeps its number, so a later step is never sent under the same one.

The feed carries every run's steps on one topic, filtered by run as each subscriber reads it, so a
skip in `sequence` reports a loss whichever run it came from, this one or another. Only the
operations view can recover a gap: to follow a run already under way, subscribe first, then read
[`operations.junctionRuns`](/docs/sdk-reference/graphql-api/queries#junctionruns) for the steps it
took before or lost, and keep for each position whichever is further along. That query sits behind
the operations gate, so a broadcast subscriber cannot read it and sees only the steps that reach it
after it subscribed.
`Trax.Api/docs/adr/0037` records why the feed follows one run.

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

### Watching a queued run

A [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) mutation called with `mode: QUEUE` returns an `externalId` and a `workQueueId`, but no `metadataId`: the run does not exist yet. The run the scheduler later dispatches carries the same `externalId` (see [Following a queued run](/docs/sdk-reference/graphql-api/mutations#following-a-queued-run)), so a client watches it like this:

1. Open the socket and subscribe to `onTrainStateChanged` **before** sending the mutation. There is no replay, and a short train can finish before a subscription opened afterwards is registered.
2. Send the mutation over HTTP and keep its `externalId`. Buffer any events that arrive before the response does.
3. Keep only events whose `externalId` matches. The lifecycle fields take no filter argument, so the client filters.
4. Stop at the first terminal state: `COMPLETED` (read `output`), `FAILED` (read `failureReason`) or `CANCELLED`.

```graphql
subscription {
  onTrainStateChanged {
    externalId
    metadataId
    trainState
    sequence
    output
    failureReason
  }
}
```

```text
mutation  -> { "externalId": "66b533589bf747f1aac661a5deeea356", "workQueueId": 2 }
event     -> { "externalId": "66b5...a356", "metadataId": 55, "trainState": "IN_PROGRESS", "sequence": 1 }
event     -> { "externalId": "66b5...a356", "metadataId": 55, "trainState": "COMPLETED", "sequence": 2, "output": { ... } }
```

What the subscriber needs and what it should expect:

- The train carries `[TraxBroadcast]` and a posture that admits the caller (for example the same `[TraxAuthorize(Roles = ...)]` that lets it call the mutation), unless the caller satisfies the operations authorization. Without it the subscription is refused with `TRAX_AUTHORIZATION`, as [Who receives what](#who-receives-what) says, even on a host that exposes operations.
- A failed delivery that the dispatcher requeues (`MaxDispatchAttempts`) creates a new execution row with the same `externalId` and emits no event; the client sees events only from the attempt a runner started. A run whose deliveries are exhausted never emits a terminal event, so give the wait a timeout.
- A `sequence` that skips a number means events were lost. An operator can look the run up in `operations.executions`; a client without that access should read the outcome from the app's own data (a [query model](/docs/sdk-reference/graphql-api/query-models) over what the train wrote).
- A queued run that fails is not retried.

A runnable C# version of this loop over `System.Net.WebSockets` is the `GraphQLWebSocketClient` in `tests/Trax.Samples.ChatService.E2E/Utilities` of [Trax.Samples](https://github.com/TraxSharp/Trax.Samples); see [Testing subscriptions](/docs/cross-cutting/e2e-testing#testing-subscriptions).

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

`Notify` never throws and never blocks the caller. Each domain is held once while it waits to be read, so repeated signals for a domain collapse into one and a burst for one domain never crowds out another's. A value outside `ChangeDomain` (a cast integer) signals nothing and logs a throttled warning. A background coalescer flushes the distinct set of changed domains to the `onDataChanged` topic. Trax's own scheduler and GraphQL write paths already call `Notify`, so the dashboard gets live updates out of the box; call it yourself only from custom write paths that should nudge a dashboard view.

## Your own subscription fields

The lifecycle fields stream train events. A domain feed of your own, such as a chat room's
messages, is a field you add to the same root and feed from a lifecycle hook. The
[Chat Service sample](/docs/samples/chat-service) is a complete one.

### Add the field

Trax's subscription root is named `LifecycleSubscriptions`, so a type extension targets that name:

```csharp
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Effect.Attributes;

[ExtendObjectType("LifecycleSubscriptions")]
public class ChatSubscriptions
{
    [TraxAuthorize(Roles = "User")]
    [Subscribe(With = nameof(SubscribeToChatEventAsync))]
    public ChatEvent OnChatEvent(Guid chatRoomId, [EventMessage] ChatEvent message) => message;

    public async ValueTask<ISourceStream<ChatEvent>> SubscribeToChatEventAsync(
        Guid chatRoomId,
        TraxCaller caller,
        ChatDbContext db,
        ITopicEventReceiver receiver,
        CancellationToken cancellationToken)
    {
        var userId = caller.Principal?.Id;
        if (userId is null || !await db.Participants.AnyAsync(
                p => p.RoomId == chatRoomId && p.UserId == userId, cancellationToken))
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Not authorized.").SetCode("TRAX_AUTHORIZATION").Build());

        return await receiver.SubscribeAsync<ChatEvent>($"ChatRoom:{chatRoomId}", cancellationToken);
    }
}

// Program.cs
builder.Services.AddTraxGraphQL(graphql => graphql.AddTypeExtension<ChatSubscriptions>());
```

- **Target `"LifecycleSubscriptions"`, not `OperationTypeNames.Subscription`.** HotChocolate drops an
  extension whose target type does not exist, with no startup error, so an extension of
  `"Subscription"` leaves the field out of the schema and a client's subscribe fails with "The field
  `onChatEvent` does not exist on the type `LifecycleSubscriptions`". Check the served schema with
  `{ __schema { subscriptionType { name fields { name } } } }`.
- **Declare a posture on the field.** A field on a root type inherits no gate, so the host refuses
  to start when the field carries neither `[TraxAuthorize]` nor `[TraxAllowAnonymous]` (see
  [Fields Added by a Type Extension](/docs/authorization#fields-added-by-a-type-extension)).
- **Check the record in the subscribe resolver.** `[TraxAuthorize]` decides who may use the field;
  which topic a caller may listen to (their own room, their own orders) is the resolver's job. It
  runs once, when the client subscribes, and a refusal reaches the client as an `error` message for
  that subscription. The method named by `Subscribe(With = ...)` is not exposed as a field. A
  resolver parameter of a service type, such as `TraxCaller` or a `DbContext`, is resolved from the
  container.

### Publish to it

Send to the topic from an [`ITrainLifecycleHook`](/docs/sdk-reference/configuration/add-lifecycle-hook)
when the train whose result the feed carries completes:

```csharp
public class ChatLifecycleHook(ITopicEventSender eventSender) : ITrainLifecycleHook
{
    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (metadata.Name != typeof(ISendMessageTrain).FullName || metadata.Output is null)
            return;

        using var doc = JsonDocument.Parse(metadata.Output);
        var roomId = doc.RootElement.GetProperty("chatRoomId").GetGuid();
        await eventSender.SendAsync($"ChatRoom:{roomId}", new ChatEvent(roomId, metadata.Output), ct);
    }
}

public class ChatLifecycleHookFactory(IServiceProvider services) : ITrainLifecycleHookFactory
{
    public ITrainLifecycleHook Create() => ActivatorUtilities.CreateInstance<ChatLifecycleHook>(services);
}

// Program.cs
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UseSqlite(connectionString).AddJson().AddLifecycleHook<ChatLifecycleHookFactory>())
    .AddMediator(typeof(ChatLifecycleHook).Assembly));
```

`metadata.Name` is the train's canonical name, its service interface's `FullName`.
`metadata.Output` is the output serialized as camelCase JSON, and it begins with a `"$id"`
property; read the fields you need rather than forwarding it as your schema. A hook fires for every
train, so it filters by name itself; `[TraxBroadcast]` on the train is not needed for your own hook,
only for the lifecycle fields. Events your code sends through `ITopicEventSender` carry no
`sequence` and never cause a skip in the lifecycle fields' numbering.

The in-memory transport reaches subscribers on the process that sent the event. A hook that runs on
a worker process reaches no API node's subscribers; bridge it the way the lifecycle fields are
bridged, with [`UseBroadcaster()`](/docs/sdk-reference/configuration/use-broadcaster), or keep the
train on the API node.

## WebSocket Connection

Subscriptions use the GraphQL over WebSocket protocol. Connect to the same endpoint as queries and mutations:

```
ws://localhost:5000/trax/graphql
```

In Nitro (the built-in GraphQL IDE, served in Development), subscriptions work out of the box. Just write a subscription query and execute it.

For programmatic clients, use any GraphQL client that supports the `graphql-ws` protocol (e.g., Apollo Client, urql, Strawberry Shake).

`AddTraxGraphQL()` wires the WebSocket upgrade middleware at the front of the pipeline (via an `IStartupFilter`), so the handshake upgrades no matter where you place `UseTraxGraphQL()` relative to other endpoint middleware such as `UseTraxDashboard()`. You do not need to call `app.UseWebSockets()` yourself.

A subscription releases its place on the topic as soon as it ends: when the client sends `complete`, closes the socket, or drops the connection without a close frame, and when code running a subscription in-process through `IRequestExecutor` disposes its response stream. It does not wait for the next event on the topic, so a quiet topic such as `onTrainCancelled` keeps no buffer for a subscriber that has left.

### Connection lifetime

Every connection has an end, however it authenticated. At its maximum lifetime, one hour unless you set another with `MaxConnectionLifetime(...)`, the server closes it with close code 1001 (Going Away) and the message `The connection reached its maximum lifetime.`. graphql-ws clients reconnect after 1001 by themselves and authenticate again in the new `connection_init`; the subscriptions restart, and an event published in between is not replayed. A connection also closes earlier when its credential ends, with close code 1008 (policy violation), which graphql-ws clients do not retry because the application must obtain a new credential first:

| Credential | Closed early when | Message |
|---|---|---|
| JWT | its `exp` passes | `The access token has expired.` |
| Session cookie | the sign-in's `ExpiresUtc` passes (the expiry the cookie had at the upgrade; a sliding renewal over HTTP does not reach an open socket) | `The sign-in has expired.` |
| API key | a re-check finds the key no longer resolves to the principal it connected as: revoked or rotated, or with other roles or claims, or the resolver fails | `The connection's credential is no longer valid.` |

An API-key connection's key is resolved again every five minutes, or at the interval set with `RecheckConnectionCredentialsEvery(...)`, so a revoked key's sockets close within one interval. Each re-check is one call to your `ITraxPrincipalResolver<string>` per open API-key connection, in a scope of its own. A changed display name does not close the connection. Both settings take a positive duration of at most one day; anything else throws `ArgumentOutOfRangeException`.

```csharp
services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<AppDbContext>()
    .MaxConnectionLifetime(TimeSpan.FromMinutes(30))           // default: 1 hour
    .RecheckConnectionCredentialsEvery(TimeSpan.FromMinutes(1))); // default: 5 minutes
```

A host that replaces the socket interceptor through `ConfigureSchema` replaces these with it.

### Allowed origins

A WebSocket upgrade that carries an `Origin` header is accepted only from origins the host serves: the endpoint's own host, or an allowed origin. Anything else is answered with `403` before the handshake. An upgrade with no `Origin` header, which is what non-browser clients send, is accepted. The allowed origins default to those your CORS default policy (`AddCors(o => o.AddDefaultPolicy(...))`) names with `WithOrigins(...)` or admits with `SetIsOriginAllowed(...)`. A default policy built with `AllowAnyOrigin()` allows no origin besides the endpoint's own: a browser sends its cookies with every WebSocket upgrade, so a socket is always a credentialed request, and CORS never lets `AllowAnyOrigin()` carry credentials. Set the origins explicitly with `AllowSocketOrigins(...)` on the GraphQL builder, which replaces the CORS default:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<AppDbContext>()
    .AllowSocketOrigins("https://app.example.com", "https://admin.example.com"));
```

The endpoint's own host is compared without its scheme, so an https page served through a TLS-terminating proxy is recognised. The check applies wherever the Trax schema serves a socket, whether `UseTraxGraphQL()` maps it or you map it yourself with `MapGraphQL(path, "trax")`, and it does not apply to another HotChocolate schema on the same host. A host whose browser clients are on another origin, and whose CORS policy is a named one or allows any origin, needs `AllowSocketOrigins(...)`.

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

A refused connection is closed before the `connection_ack`. A connection with no credential at all,
on a host with a token scheme, is closed with code `4403` and the reason
`Missing auth token in connection_init payload.` The payload key must be one of those above: a
client that sends its key as `{ "X-Api-Key": "..." }` (the HTTP header's name) has sent no credential
and is closed the same way. With `graphql-ws`, that is `connectionParams: { apiKey }`.

The schemes are read from the finished container on the first connection, so it does not matter whether your `AddTrax*Auth` call comes before or after `AddTraxGraphQL()`.

A JWT in `connection_init` is authenticated by the scheme's own `JwtBearerHandler`, exactly as an HTTP request carrying it as `Authorization: Bearer` would be. The handler runs against a request built from the upgrade request (its path, query, headers and addresses), so everything that applies over HTTP applies on the socket:

- signature, issuer, audience, lifetime and clock skew, from the scheme's `JwtBearerOptions`;
- Authority/JWKS schemes (Cognito, Google, any OIDC provider), including the refresh of the JWKS when a token is signed with a key id the cached document does not have, so a rotated key is picked up;
- your `JwtBearerEvents`, set through `CustomizeBearerOptions`: `OnMessageReceived`, `OnTokenValidated` (a revocation or tenant check that calls `context.Fail(...)` refuses the socket), `OnAuthenticationFailed`;
- the claim mapping the handler applies (`sub` arrives as `ClaimTypes.NameIdentifier` unless `MapInboundClaims` is off), and any `IClaimsTransformation`.

A refused connection is told only `Invalid JWT.`; the handler's reason is logged, not sent, as HTTP returns only a 401.

**A connection is closed when its token expires.** A socket outlives the moment its token was checked, so at the token's `exp` the server closes it with close code 1008 (policy violation) and the message `The access token has expired.`. An HTTP request with that token would be refused from then on. The client reconnects with a fresh token, which `connection_init` carries on every reconnect. Revoking a user does not close a JWT socket that is already open; the token's lifetime and the [connection lifetime](#connection-lifetime) bound how long it stays open, so keep access tokens short-lived.

Each connection gets its own DI scope. The interceptor itself is a singleton (HotChocolate builds one per schema), so it opens a scope when `connection_init` arrives, runs the handler and your scoped `ITraxPrincipalResolver<T>` inside it, and disposes it once the principal is resolved. A resolver holding a `DbContext` works on subscriptions exactly as it does on HTTP. The principal is captured onto the connection's `HttpContext.User` and reused for every subsequent operation until the connection closes.

Cookie auth (`Trax.Api.Auth.Oidc`) needs no interceptor. The browser sends cookies on the upgrade request and the cookie scheme authenticates it like any HTTP request.

That holds only when no token scheme is registered. Once `AddTraxApiKeyAuth`, `AddTraxJwtAuth` or `AddTraxJwtDispatcher` is registered, every connection needs a credential in `connection_init`, and an upgrade that is already authenticated by a session cookie is rejected without one. A browser app on a host with both cookie and token auth sends its token in the payload.

This includes a subscriber that only wants `[TraxAllowAnonymous]` trains. On a host with a token scheme, a `connection_init` with no credential is refused before any subscription is made, whatever the trains it would have received allow. A host whose demo keys exist only in Development therefore accepts anonymous sockets outside Development (no scheme is registered there) and refuses them in Development.

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

Data-change signals ride the same bridge. In a single-process deployment (the API collocated with the scheduler), `onDataChanged` works with no broadcaster: signals flow in-process to the subscription topic. When the scheduler runs in a separate process, `UseBroadcaster()` forwards its `Notify` calls over the message bus (a `BroadcastChangeSink`), and the API's `GraphQLDataChangeHandler` re-publishes them to local subscribers. The originating host ignores its own broadcast via the same instance-id de-duplication used for lifecycle events, so replicas of one app still receive each other's signals.

See [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) for full details.

## Package

```
dotnet add package Trax.Api.GraphQL
```
