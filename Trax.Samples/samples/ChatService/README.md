# Trax Chat Service Sample

A single-server chat application demonstrating how Trax lifecycle hooks drive domain-specific real-time GraphQL subscriptions. When a chat mutation train completes, a custom `ITrainLifecycleHook` publishes the result to a room-scoped HotChocolate topic, and any client subscribed to that room receives the event via WebSocket.

## Architecture

```
ChatService/
├── Trax.Samples.ChatService.Data/       EF Core entities, DbContext, migrations (chat schema)
├── Trax.Samples.ChatService/            Trains, lifecycle hook, subscription types, auth
├── Trax.Samples.ChatService.Api/        Single-server ASP.NET Core host
└── Trax.Samples.ChatService.Client/     React + TypeScript frontend (Apollo Client)
```

Everything runs in one process — no scheduler, no workers. The data layer uses a separate `chat` schema that coexists with Trax's `trax` schema in the same Postgres database.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) (for the React client)
- Docker (for PostgreSQL)

## Running

```bash
# 1. Start PostgreSQL
cd Trax.Samples && docker compose up -d

# 2. Pack local Trax packages (if not already done)
./pack-local.sh

# 3. Start the API
dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api

# 4. Start the React client (separate terminal)
cd samples/ChatService/Trax.Samples.ChatService.Client
npm install
npm run dev
```

- GraphQL IDE (Banana Cake Pop): http://localhost:5210/trax/graphql
- React client: http://localhost:5173

## Authentication

Fake API key authentication via `X-Api-Key` header (for demonstration only):

| API Key       | User ID   | Display Name |
|---------------|-----------|--------------|
| `alice-key`   | `alice`   | Alice        |
| `bob-key`     | `bob`     | Bob          |
| `charlie-key` | `charlie` | Charlie      |

The React client provides a dropdown to switch between users. Open multiple browser tabs to simulate different users chatting in real time.

## Quick Walkthrough (GraphQL IDE)

```graphql
# 1. Create a room (as Alice — set X-Api-Key: alice-key)
mutation {
  dispatch {
    createChatRoom(input: { name: "General", userId: "alice", displayName: "Alice" }) {
      externalId
      output { chatRoomId name }
    }
  }
}

# 2. Join the room (as Bob — set X-Api-Key: bob-key)
mutation {
  dispatch {
    joinChatRoom(input: { chatRoomId: "<id>", userId: "bob", displayName: "Bob" }) {
      externalId
      output { joinedAt }
    }
  }
}

# 3. Subscribe to real-time events (in a second tab)
subscription {
  onChatEvent(chatRoomId: "<id>") {
    eventType
    payload
    timestamp
  }
}

# 4. Send a message — the subscription tab receives it
mutation {
  dispatch {
    sendMessage(input: { chatRoomId: "<id>", senderUserId: "alice", content: "Hello!" }) {
      externalId
      output { messageId content sentAt }
    }
  }
}

# 5. Query chat history
{
  discover {
    getChatHistory(input: { chatRoomId: "<id>" }) {
      messages { senderDisplayName content sentAt }
    }
  }
}
```

## How the Lifecycle Hook Works

1. Chat mutation trains (`CreateChatRoom`, `JoinChatRoom`, `SendMessage`) are decorated with `[TraxBroadcast]`, which causes lifecycle hooks to fire on completion.
2. `ChatLifecycleHook` implements `ITrainLifecycleHook` and checks `metadata.Name` against a map of chat train interfaces.
3. On match, it parses `metadata.Output` (serialized JSON) to extract the `chatRoomId`.
4. It publishes a `ChatSubscriptionEvent` to the HotChocolate topic `"ChatRoom:{chatRoomId}"`.
5. Clients subscribed via `onChatEvent(chatRoomId: "...")` receive the event in real time.

This coexists with the built-in `GraphQLSubscriptionHook` — both hooks fire for `[TraxBroadcast]` trains.

## Tests

```bash
dotnet test tests/Trax.Samples.ChatService.Tests
```

31 tests covering lifecycle hook behavior (10 unit) and all train steps (21 integration using EF Core in-memory provider).
