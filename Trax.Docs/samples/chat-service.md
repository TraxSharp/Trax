---
layout: default
title: Chat Service
description: "The ChatService sample: a custom GraphQL subscription fed by a lifecycle hook, socket authentication, per-subscriber checks and caller identity."
parent: Samples & Deployment
nav_order: 4
---

# Chat Service

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

`samples/ChatService` is a chat server whose messages reach the other participants over GraphQL
subscriptions. Chat mutations are ordinary Trax trains. When one completes, a lifecycle hook
publishes its output to a topic for the room, and every participant subscribed with
`onChatEvent(chatRoomId:)` receives it on their WebSocket. Everything runs in one process, on two
SQLite files, so it needs no Docker.

## What it proves

| Feature | Where |
|---|---|
| A subscription field of your own on Trax's subscription root, `LifecycleSubscriptions` | `Subscriptions/ChatSubscriptions.cs` |
| Publishing to that field from an `ITrainLifecycleHook` when a chat train completes | `Hooks/ChatLifecycleHook.cs` |
| Socket authentication: the API key in `connection_init`, a socket without one closed with `4403` | `ChatEventSubscriptionTests` |
| Per-subscriber authorization: `[TraxAuthorize]` on the field plus a participant check when subscribing | `ChatSubscriptions.SubscribeToChatEventAsync` |
| Caller identity: every train is `[TraxAuthorize]`, reads the caller from `TraxPrincipal`, and no input names the caller | `Trains/*/Junctions` |
| Invites: only a room's member can add someone, and the server, not the client, names who was added | `Trains/InviteToChatRoom`, `People/ChatDirectory.cs` |
| Room-scoped events only: no chat train is `[TraxBroadcast]`, so Trax's lifecycle subscriptions refuse a chat user | `InviteToChatRoomTests` | a member adds someone and the room appears in their own list; someone outside the room can neither add anyone nor list who could be added; an invite for someone the chat does not know is refused |
| `LifecycleSubscriptionTests` |

## Layout

```
samples/ChatService/
├── Trax.Samples.ChatService.Data/     EF Core entities, ChatDbContext, migrations
├── Trax.Samples.ChatService/          trains, the lifecycle hook, the subscription type, auth constants
├── Trax.Samples.ChatService.Api/      the host (Program.cs)
└── Trax.Samples.ChatService.Client/   React + Apollo Client, graphql-ws for subscriptions
```

## Run

From the `Trax.Samples` root:

```bash
# The API, in Development, on http://localhost:5210
dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api

# Optional: the React client on http://localhost:5173
cd samples/ChatService/Trax.Samples.ChatService.Client
npm ci
npm run dev
```

`dotnet run` starts the API in Development through `Properties/launchSettings.json`, which is the
only environment that registers the demo keys. Open the React client in two tabs and pick Alice in
one and Bob in the other. As Alice, create a room and press **+ Invite** to add Bob: the room appears
in Bob's list within a few seconds, marked new, and the two of you can chat.

The client's **Under the hood** panel lists what each tab sends to Trax and gets back as it happens: every
GraphQL request and response, and every WebSocket frame. A message can be followed from the
`dispatch.sendMessage` mutation that runs its `SendMessage` train to the `MessageSent` event that
`ChatLifecycleHook` publishes for the same run; the entries share the run's external id. The API key in
`connection_init` is shown masked.

## Try it

The demo keys are `alice-key-do-not-use-in-production`, `bob-key-do-not-use-in-production` and
`charlie-key-do-not-use-in-production`. Each resolves to a principal whose id Trax qualifies with
the scheme, so Alice is `TraxApiKey:alice`.

```bash
G=http://localhost:5210/trax/graphql

# 1. Alice creates a room
curl -s $G -H 'Content-Type: application/json' -H 'X-Api-Key: alice-key-do-not-use-in-production' \
  -d '{"query":"mutation { dispatch { createChatRoom(input: { name: \"General\" }) { output { chatRoomId name } } } }"}'
# {"data":{"dispatch":{"createChatRoom":{"output":{"chatRoomId":"35427876-...","name":"General"}}}}}

ROOM=<chatRoomId from step 1>

# 2. Alice adds Bob (Bob could also join by the room's id himself, with joinChatRoom)
curl -s $G -H 'Content-Type: application/json' -H 'X-Api-Key: alice-key-do-not-use-in-production' \
  -d "{\"query\":\"mutation { dispatch { inviteToChatRoom(input: { chatRoomId: \\\"$ROOM\\\", userId: \\\"TraxApiKey:bob\\\" }) { output { userId displayName invitedByDisplayName } } } }\"}"
# {"data":{"dispatch":{"inviteToChatRoom":{"output":{"userId":"TraxApiKey:bob","displayName":"Bob","invitedByDisplayName":"Alice"}}}}}
```

3. Subscribe as Bob. Any `graphql-ws` client works; the key goes in the `connection_init` payload
   as `apiKey`. From Node, save this as `subscribe.mjs` in the client folder (which has
   `graphql-ws` installed) and run `node subscribe.mjs $ROOM`:

   ```js
   import { createClient } from "graphql-ws";
   const client = createClient({
     url: "ws://localhost:5210/trax/graphql",
     connectionParams: { apiKey: "bob-key-do-not-use-in-production" },
   });
   client.subscribe(
     { query: `subscription { onChatEvent(chatRoomId: "${process.argv[2]}") { eventType payload } }` },
     { next: (m) => console.log(JSON.stringify(m)), error: console.error, complete: () => {} },
   );
   ```

```bash
# 4. Alice sends a message
curl -s $G -H 'Content-Type: application/json' -H 'X-Api-Key: alice-key-do-not-use-in-production' \
  -d "{\"query\":\"mutation { dispatch { sendMessage(input: { chatRoomId: \\\"$ROOM\\\", content: \\\"Hello!\\\" }) { output { messageId senderUserId content } } } }\"}"
```

Bob's subscription prints:

```json
{"data":{"onChatEvent":{"eventType":"MessageSent","payload":"{\"$id\":\"1\",\"messageId\":\"4738eb9f-...\",\"chatRoomId\":\"35427876-...\",\"senderUserId\":\"TraxApiKey:alice\",\"senderDisplayName\":\"Alice\",\"content\":\"Hello!\",\"sentAt\":\"2026-10-03T16:38:49.03Z\"}"}}}
```

```bash
# 5. Charlie never joined: his read is refused, and so is his subscription
curl -s $G -H 'Content-Type: application/json' -H 'X-Api-Key: charlie-key-do-not-use-in-production' \
  -d "{\"query\":\"{ discover { getChatHistory(input: { chatRoomId: \\\"$ROOM\\\" }) { messages { content } } } }\"}"
# {"errors":[{"message":"You are not a participant in room 35427876-....","extensions":{"code":"TRAX_TRAIN_ERROR"}}],...}

# 6. Without a key, every operation is refused
curl -s $G -H 'Content-Type: application/json' \
  -d "{\"query\":\"{ discover { getChatHistory(input: { chatRoomId: \\\"$ROOM\\\" }) { messages { content } } } }\"}"
# {"errors":[{"message":"Not authorized.","extensions":{"code":"TRAX_AUTHORIZATION"}}],...}

# 7. Bob's rooms: the query takes no input, it lists the caller's own
curl -s $G -H 'Content-Type: application/json' -H 'X-Api-Key: bob-key-do-not-use-in-production' \
  -d '{"query":"{ discover { getChatRooms { rooms { id name participantCount lastMessageAt } } } }"}'
```

Charlie's `node subscribe.mjs` (with his key) prints `error [{"message":"Not authorized.","extensions":{"code":"TRAX_AUTHORIZATION"}}]`,
and a client that sends no key at all is closed with `4403 Missing auth token in connection_init payload.`
before it can subscribe.

## How it works

### The host

```csharp
using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ChatDbContext>(o => o.UseSqlite("Data Source=chat.db"));

// Demo keys, Development only. The factory overload sets a display name.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add("alice-key-do-not-use-in-production", () => new TraxPrincipal("alice", "Alice", ["User"]))
            .Add("bob-key-do-not-use-in-production", () => new TraxPrincipal("bob", "Bob", ["User"])));
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects
            .UseSqlite("Data Source=trax.db")
            .AddJson()
            .SaveTrainParameters()
            .AddLifecycleHook<ChatLifecycleHookFactory>())
        .AddMediator(typeof(ChatLifecycleHookFactory).Assembly));

builder.Services.AddTraxGraphQL(graphql => graphql.AddTypeExtension<ChatSubscriptions>());

// The CORS default policy's origins are also the origins a browser socket is accepted from.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.Run();
```

Packages: `Trax.Api`, `Trax.Api.GraphQL`, `Trax.Api.Auth.ApiKey`, `Trax.Effect.Data.Sqlite`,
`Trax.Effect.Provider.Json`, `Trax.Effect.Provider.Parameter`, `Trax.Mediator`, and
`HotChocolate.AspNetCore` in the library that defines the subscription type.

### The trains act as the caller

Every train is `[TraxAuthorize(Roles = "User")]`, and a junction that needs the user injects
[`TraxPrincipal`](/docs/sdk-reference/api-auth/injecting-trax-principal). The gate refuses an
anonymous request before the junction is built, so the injection cannot fail at run time. The inputs
carry no user field, which is what makes it impossible to act for somebody else:

```csharp
public record SendMessageInput
{
    public Guid ChatRoomId { get; init; }
    public required string Content { get; init; }
}

public class ValidateSenderJunction(ChatDbContext db, TraxPrincipal caller)
    : Junction<SendMessageInput, SendMessageInput>
{
    public override async Task<SendMessageInput> Run(SendMessageInput input)
    {
        if (!await db.ChatParticipants.AnyAsync(p =>
                p.ChatRoomId == input.ChatRoomId && p.UserId == caller.Id))
            throw new TrainException($"You are not a participant in room {input.ChatRoomId}.");
        return input;
    }
}
```

`caller.Id` is the qualified id (`TraxApiKey:alice`), so that is what the sample stores. A
`TrainException`'s message reaches the client as written; any other exception's message does not.
`GetChatRoomsInput` is an empty record, so `getChatRooms` takes no `input` argument.

### Inviting someone

`inviteToChatRoom` is the one input that names a user, and only as the person to add. Its first
junction admits the invite only from a member of the room, only for someone the host's
`ChatDirectory` knows, and only for someone not in the room already; its second adds them under the
name the directory holds, so the inviter cannot choose what the room calls them. The host builds the
directory from the same people it registers credentials for, with each id qualified the way Trax
qualifies the caller's:

```csharp
builder.Services.AddSingleton(
    new ChatDirectory(
        demoPeople.Select(p => new ChatPerson(
            TraxPrincipalId.Qualify(Trax.Api.Auth.ApiKey.ApiKeyDefaults.SchemeName, p.Id),
            p.Name
        ))
    )
);
```

`getChatRoomPeople(input: { chatRoomId })` lists everyone else in the directory, each marked as in the
room or not, to a member of the room. `ChatLifecycleHook` publishes a completed invite as
`UserJoined`, so every member's open room shows "Alice added Bob" as it happens. The invitee's room
list is a query the client polls, so the room appears there on its next refresh.

### Publishing a room's events

The hook runs after every train. It ignores all but the four chat mutations (an invite publishes as
`UserJoined`, like a join), reads the room id out of the serialized output, and sends to that room's
topic:

```csharp
public class ChatLifecycleHook(ITopicEventSender eventSender) : ITrainLifecycleHook
{
    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (!TrainEventTypes.TryGetValue(metadata.Name, out var eventType) || metadata.Output is null)
            return;

        using var doc = JsonDocument.Parse(metadata.Output);
        if (!doc.RootElement.TryGetProperty("chatRoomId", out var roomId))
            return;

        var chatEvent = new ChatSubscriptionEvent(
            roomId.GetGuid(), eventType, metadata.Output,
            metadata.EndTime ?? DateTime.UtcNow, metadata.ExternalId);
        await eventSender.SendAsync(ChatSubscriptions.Topic(roomId.GetGuid()), chatEvent, ct);
    }
}
```

A lifecycle hook fires for every train, so it filters by name itself: `metadata.Name` is the
train's canonical name, the service interface's `FullName` (`typeof(ISendMessageTrain).FullName`).
The hook does not depend on `[TraxBroadcast]`, and no chat train carries it: a broadcast train's
`onTrainCompleted` delivers every run's output to every caller the train admits, so every user would
read every room's messages. Trax's lifecycle subscriptions therefore refuse a chat user, and
`onChatEvent` is the only feed. `metadata.Output` is camelCase JSON and starts with a
`"$id"` property, which the client ignores. The hook is registered with
`AddLifecycleHook<ChatLifecycleHookFactory>()`, a factory that builds it with
`ActivatorUtilities`, so it can take `ITopicEventSender` from the container.

### The subscription field

```csharp
[ExtendObjectType("LifecycleSubscriptions")]
public class ChatSubscriptions
{
    public static string Topic(Guid chatRoomId) => $"ChatRoom:{chatRoomId}";

    [TraxAuthorize(Roles = "User")]
    [Subscribe(With = nameof(SubscribeToChatEventAsync))]
    public ChatSubscriptionEvent OnChatEvent(Guid chatRoomId, [EventMessage] ChatSubscriptionEvent message)
        => message;

    public async ValueTask<ISourceStream<ChatSubscriptionEvent>> SubscribeToChatEventAsync(
        Guid chatRoomId, TraxCaller caller, ChatDbContext db,
        ITopicEventReceiver receiver, CancellationToken cancellationToken)
    {
        var userId = caller.Principal?.Id;
        var isParticipant = userId is not null && await db.ChatParticipants.AnyAsync(
            p => p.ChatRoomId == chatRoomId && p.UserId == userId, cancellationToken);
        if (!isParticipant)
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Not authorized.").SetCode("TRAX_AUTHORIZATION").Build());

        return await receiver.SubscribeAsync<ChatSubscriptionEvent>(Topic(chatRoomId), cancellationToken);
    }
}
```

Three things in it are load-bearing:

- **The target name.** Trax's subscription root is named `LifecycleSubscriptions`. An extension of
  `OperationTypeNames.Subscription` (`"Subscription"`) targets a type that does not exist, so
  Trax refuses to start the host, naming the extension and telling you to extend
  `LifecycleSubscriptions` instead. HotChocolate alone would drop the extension without an error,
  leaving the field missing from the schema; the sample shipped that way until an E2E test
  subscribed for real.
- **The posture.** A field added to a root type inherits no gate, so Trax refuses to start a host
  whose extension field declares neither `[TraxAuthorize]` nor `[TraxAllowAnonymous]`.
- **The subscribe resolver.** `[TraxAuthorize]` decides who may use the field at all; which rooms
  a caller may listen to is the resolver's job, checked once when the subscription is made. The
  method named by `Subscribe(With = ...)` does not become a field of its own.

See [Subscriptions: your own subscription fields](/docs/sdk-reference/graphql-api/subscriptions#your-own-subscription-fields)
for the general recipe.

### The browser client

Apollo splits traffic: HTTP for queries and mutations with an `X-Api-Key` header, `graphql-ws` for
subscriptions with the key in `connectionParams`. Browsers cannot set a header on a WebSocket
upgrade, so the key must be in the payload, under `apiKey` or `authToken`. A key under any other
name (the client once sent `"X-Api-Key"`) leaves the socket without a credential, and it is closed
with `4403`.

## Tests

```bash
dotnet test tests/Trax.Samples.ChatService.Tests   # junctions and the hook, in memory (40 tests)
dotnet test tests/Trax.Samples.ChatService.E2E     # the real host over HTTP and WebSocket, SQLite (53 tests)
```

| Class | Proves |
|---|---|
| `ChatEventSubscriptionTests` | `onChatEvent` is in the served schema, reaches the sender and another participant with the sender's identity, carries nothing from another room, refuses a non-participant and closes an anonymous socket with `4403`; a non-participant receives none of a room's messages on any lifecycle field |
| `ChatCallerIdentityTests` | no anonymous operation, no reading or posting in a room you have not joined, a message is stored as its sender, rooms list only your own |
| `LifecycleSubscriptionTests` | Trax's own lifecycle fields (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`, `onTrainStateChanged`) refuse a chat user, because no chat train is `[TraxBroadcast]` |

The subscription tests never wait on a fixed delay: they keep sending until the first event arrives,
which proves the subscription is live, then assert on the next one.

## SDK Reference

> [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [TraxBroadcast](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxCaller](/docs/sdk-reference/api-auth/trax-caller) | [Injecting TraxPrincipal](/docs/sdk-reference/api-auth/injecting-trax-principal) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
