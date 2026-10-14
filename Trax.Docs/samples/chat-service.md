---
title: Chat Service
description: "The ChatService sample: a custom GraphQL subscription fed by a lifecycle hook, socket authentication, per-subscriber checks and caller identity."
type: explanation
area: samples
nav_order: 2
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
| Room-scoped events only: no chat train is `[TraxBroadcast]`, so Trax's lifecycle subscriptions refuse a chat user | `LifecycleSubscriptionTests` |

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

### Layout

```
samples/ChatService/
├── Trax.Samples.ChatService.Data/     EF Core entities, ChatDbContext, migrations
├── Trax.Samples.ChatService/          trains, the lifecycle hook, the subscription type, auth constants
├── Trax.Samples.ChatService.Api/      the host (Program.cs)
└── Trax.Samples.ChatService.Client/   React + Apollo Client, graphql-ws for subscriptions
```

### The host

`Program.cs`, less its banner comment and the `partial class Program` the tests need:

```csharp from=Trax.Samples/samples/ChatService/Trax.Samples.ChatService.Api/Program.cs
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
using Trax.Samples.ChatService.Auth;
using Trax.Samples.ChatService.Data;
using Trax.Samples.ChatService.Hooks;
using Trax.Samples.ChatService.People;
using Trax.Samples.ChatService.Subscriptions;
using SampleKeys = Trax.Samples.ChatService.Auth.ApiKeyDefaults;

var builder = WebApplication.CreateBuilder(args);

var traxConnectionString =
    builder.Configuration.GetConnectionString("TraxDatabase") ?? "Data Source=trax.db";

var chatConnectionString =
    builder.Configuration.GetConnectionString("ChatDatabase") ?? "Data Source=chat.db";

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Chat data layer ─────────────────────────────────────────────────────────
builder.Services.AddDbContext<ChatDbContext>(options => options.UseSqlite(chatConnectionString));

// ── Authentication, fake API key for demonstration (NO WARRANTY, see SECURITY-DISCLAIMER.md) ──
// The demo keys are published in this repository, so they are registered only in Development
// (Properties/launchSettings.json sets it for `dotnet run`). Anywhere else no credential exists
// until you register real ones, and every [TraxAuthorize] operation is refused.
// The callers, and the people a member can invite, are the same three: the directory is built from
// them, and is empty wherever no credential is registered.
var alice = ("alice", "Alice");
var bob = ("bob", "Bob");
var charlie = ("charlie", "Charlie");
(string Id, string Name)[] demoPeople = builder.Environment.IsDevelopment()
    ? [alice, bob, charlie]
    : [];

if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(SampleKeys.AliceKey, () => ChatUser(alice))
            .Add(SampleKeys.BobKey, () => ChatUser(bob))
            .Add(SampleKeys.CharlieKey, () => ChatUser(charlie))
    );

// Trax qualifies a principal's id with the scheme that authenticated it, and the directory holds the
// qualified form, the one a junction reads from TraxPrincipal.Id.
builder.Services.AddSingleton(
    new ChatDirectory(
        demoPeople.Select(p => new ChatPerson(
            TraxPrincipalId.Qualify(Trax.Api.Auth.ApiKey.ApiKeyDefaults.SchemeName, p.Id),
            p.Name
        ))
    )
);
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// ── Register Trax Effect + Mediator + ChatLifecycleHook ─────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UseSqlite(traxConnectionString)
                .AddJson()
                .SaveTrainParameters()
                .AddLifecycleHook<ChatLifecycleHookFactory>()
        )
        .AddMediator(typeof(ChatLifecycleHookFactory).Assembly)
);

// ── Register GraphQL API + chat subscriptions ───────────────────────────────
builder.Services.AddTraxGraphQL(graphql => graphql.AddTypeExtension<ChatSubscriptions>());

builder.Services.AddHealthChecks().AddTraxHealthCheck();

// ── CORS: allow the React dev server ────────────────────────────────────
// The default policy's origins are also the origins a browser WebSocket upgrade is accepted
// from, so the React client's subscriptions connect without AllowSocketOrigins(...).
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
    )
);

var app = builder.Build();

// ── Auto-migrate chat schema ────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    await db.Database.MigrateAsync();
}

// ── Map endpoints ───────────────────────────────────────────────────────────
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();

// The display name is what other participants see beside a message.
static TraxPrincipal ChatUser((string Id, string Name) person) =>
    new(person.Id, person.Name, [nameof(ChatRole.User)]);
```

Packages: `Trax.Api`, `Trax.Api.GraphQL`, `Trax.Api.Auth.ApiKey`, `Trax.Effect.Data.Sqlite`,
`Trax.Effect.Provider.Json`, `Trax.Effect.Provider.Parameter`, `Trax.Mediator`, and
`HotChocolate.AspNetCore` in the library that defines the subscription type. Both connection strings
fall back to SQLite files in the working directory: `trax.db` for Trax's metadata, `chat.db` for the
chat data, which the host migrates at startup.

### The trains act as the caller

Every train is `[TraxAuthorize(Roles = "User")]`, and a junction that needs the user injects
[`TraxPrincipal`](/docs/security/inject-the-current-principal). The gate refuses an
anonymous request before the junction is built, so the injection cannot fail at run time. The inputs
carry no user field, which is what makes it impossible to act for somebody else:

```csharp from=Trax.Samples/samples/ChatService/Trax.Samples.ChatService/Trains/SendMessage/SendMessageInput.cs
public record SendMessageInput
{
    public Guid ChatRoomId { get; init; }
    public required string Content { get; init; }
}
```

```csharp from=Trax.Samples/samples/ChatService/Trax.Samples.ChatService/Trains/SendMessage/Junctions/ValidateSenderJunction.cs
public class ValidateSenderJunction(ChatDbContext db, TraxPrincipal caller)
    : Junction<SendMessageInput, SendMessageInput>
{
    public override async Task<SendMessageInput> Run(SendMessageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Content))
            throw new TrainException("Message content cannot be empty.");

        if (
            !await db.ChatParticipants.AnyAsync(p =>
                p.ChatRoomId == input.ChatRoomId && p.UserId == caller.Id
            )
        )
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
qualifies the caller's (the `ChatDirectory` registration in the host above).

`getChatRoomPeople(input: { chatRoomId })` lists everyone else in the directory, each marked as in the
room or not, to a member of the room. `ChatLifecycleHook` publishes a completed invite as
`UserJoined`, so every member's open room shows "Alice added Bob" as it happens. The invitee's room
list is a query the client polls, so the room appears there on its next refresh.

### Publishing a room's events

The hook runs after every train. It ignores all but the four chat mutations (an invite publishes as
`UserJoined`, like a join), reads the room id out of the serialized output (an output it cannot
parse, or one with no room id, is skipped rather than failing the run), and sends to that room's
topic:

```csharp from=Trax.Samples/samples/ChatService/Trax.Samples.ChatService/Hooks/ChatLifecycleHook.cs
public class ChatLifecycleHook(ITopicEventSender eventSender) : ITrainLifecycleHook
{
    private static readonly Dictionary<string, string> TrainEventTypes = new()
    {
        [typeof(ISendMessageTrain).FullName!] = "MessageSent",
        [typeof(ICreateChatRoomTrain).FullName!] = "RoomCreated",
        [typeof(IJoinChatRoomTrain).FullName!] = "UserJoined",
        [typeof(IInviteToChatRoomTrain).FullName!] = "UserJoined",
    };

    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (
            !TrainEventTypes.TryGetValue(metadata.Name, out var eventType)
            || metadata.Output is null
        )
            return;

        Guid chatRoomId;
        try
        {
            using var doc = JsonDocument.Parse(metadata.Output);
            if (
                !doc.RootElement.TryGetProperty("chatRoomId", out var roomIdElement)
                && !doc.RootElement.TryGetProperty("ChatRoomId", out roomIdElement)
            )
                return;

            chatRoomId = roomIdElement.GetGuid();
        }
        catch (JsonException)
        {
            return;
        }

        var chatEvent = new ChatSubscriptionEvent(
            ChatRoomId: chatRoomId,
            EventType: eventType,
            Payload: metadata.Output,
            Timestamp: metadata.EndTime ?? DateTime.UtcNow,
            TrainExternalId: metadata.ExternalId
        );

        await eventSender.SendAsync(ChatSubscriptions.Topic(chatRoomId), chatEvent, ct);
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

```csharp from=Trax.Samples/samples/ChatService/Trax.Samples.ChatService/Subscriptions/ChatSubscriptions.cs
[ExtendObjectType("LifecycleSubscriptions")]
public class ChatSubscriptions
{
// ...
    public static string Topic(Guid chatRoomId) => $"ChatRoom:{chatRoomId}";

    [TraxAuthorize(Roles = nameof(ChatRole.User))]
    [Subscribe(With = nameof(SubscribeToChatEventAsync))]
    public ChatSubscriptionEvent OnChatEvent(
        Guid chatRoomId,
        [EventMessage] ChatSubscriptionEvent message
    ) => message;

// ...
    public async ValueTask<ISourceStream<ChatSubscriptionEvent>> SubscribeToChatEventAsync(
        Guid chatRoomId,
        TraxCaller caller,
        ChatDbContext db,
        ITopicEventReceiver receiver,
        CancellationToken cancellationToken
    )
    {
        var userId = caller.Principal?.Id;
        var isParticipant =
            userId is not null
            && await db.ChatParticipants.AnyAsync(
                p => p.ChatRoomId == chatRoomId && p.UserId == userId,
                cancellationToken
            );

        if (!isParticipant)
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Not authorized.")
                    .SetCode("TRAX_AUTHORIZATION")
                    .Build()
            );

        return await receiver.SubscribeAsync<ChatSubscriptionEvent>(
            Topic(chatRoomId),
            cancellationToken
        );
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

See [Subscriptions: your own subscription fields](/docs/api/add-a-subscription-field)
for the general recipe.

### The browser client

Apollo splits traffic: HTTP for queries and mutations with an `X-Api-Key` header, `graphql-ws` for
subscriptions with the key in `connectionParams`. Browsers cannot set a header on a WebSocket
upgrade, so the key must be in the payload, under `apiKey` or `authToken`. A key under any other
name (the client once sent `"X-Api-Key"`) leaves the socket without a credential, and it is closed
with `4403`.

```ts from=Trax.Samples/samples/ChatService/Trax.Samples.ChatService.Client/src/apollo.ts
const wsLink = new GraphQLWsLink(
  createClient({
    url: WS_URL,
    webSocketImpl: loggingWebSocket(displayName),
    // Browsers cannot set headers on a WebSocket upgrade, so the key travels in the
    // connection_init payload. Trax reads it from "apiKey" (or "authToken").
    connectionParams: { apiKey },
  }),
);
```

## Tests

```bash
dotnet test tests/Trax.Samples.ChatService.Tests   # junctions and the hook, in memory
dotnet test tests/Trax.Samples.ChatService.E2E     # the real host over HTTP and WebSocket, SQLite
```

| Class | Proves |
|---|---|
| `ChatEventSubscriptionTests` | `onChatEvent` is in the served schema, reaches the sender and another participant with the sender's identity, carries nothing from another room, refuses a non-participant and closes an anonymous socket with `4403`; a non-participant receives none of a room's messages on any lifecycle field |
| `ChatCallerIdentityTests` | no anonymous operation, no reading or posting in a room you have not joined, a message is stored as its sender, rooms list only your own |
| `InviteToChatRoomTests` | a member adds someone and the room appears in their own list; someone outside the room can neither add anyone nor list who could be added; an invite for someone the chat does not know is refused |
| `LifecycleSubscriptionTests` | Trax's own lifecycle fields (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`, `onTrainStateChanged`) refuse a chat user, because no chat train is `[TraxBroadcast]` |

The subscription tests never wait on a fixed delay: they keep sending until the first event arrives,
which proves the subscription is live, then assert on the next one.

## SDK Reference

> [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [TraxBroadcast](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxCaller](/docs/sdk-reference/api-auth/trax-caller) | [Injecting TraxPrincipal](/docs/security/inject-the-current-principal) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
