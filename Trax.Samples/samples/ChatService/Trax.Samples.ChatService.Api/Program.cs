// ─────────────────────────────────────────────────────────────────────────────
// Trax Chat Service — GraphQL API with Real-Time Subscriptions
//
// A single-server chat application powered by HotChocolate GraphQL.
// Demonstrates how Trax lifecycle hooks can drive domain-specific real-time
// subscriptions: when a SendMessage train completes, the ChatLifecycleHook
// publishes the message to a room-scoped topic, and any client subscribed
// to that room receives the event via WebSocket.
//
// Authentication: fake API key via X-Api-Key header (for demonstration only)
//   alice-key   → user "alice"
//   bob-key     → user "bob"
//   charlie-key → user "charlie"
//
// Prerequisites:
//   1. Pack local:      ./pack-local.sh
//   2. Start API:       dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api
//
// No Docker or Postgres required — uses SQLite for both Trax metadata and chat data.
//
// Try it:
//   Open http://localhost:5210/trax/graphql in a browser for Banana Cake Pop IDE
//
//   # Create a chat room (as Alice)
//   mutation { dispatch { createChatRoom(input: { name: "General", userId: "alice", displayName: "Alice" }) { externalId output { chatRoomId name } } } }
//
//   # Join the room (as Bob)
//   mutation { dispatch { joinChatRoom(input: { chatRoomId: "<id>", userId: "bob", displayName: "Bob" }) { externalId output { joinedAt } } } }
//
//   # Send a message
//   mutation { dispatch { sendMessage(input: { chatRoomId: "<id>", senderUserId: "alice", content: "Hello!" }) { externalId output { messageId content sentAt } } } }
//
//   # Subscribe to real-time chat events (in Banana Cake Pop):
//   subscription { onChatEvent(chatRoomId: "<id>") { eventType payload timestamp } }
//
//   # Query chat history
//   { discover { getChatHistory(input: { chatRoomId: "<id>" }) { messages { senderDisplayName content sentAt } } } }
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
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
builder.Services.AddTraxApiKeyAuth(keys =>
    keys.Add(SampleKeys.AliceKey, id: "alice", nameof(ChatRole.User))
        .Add(SampleKeys.BobKey, id: "bob", nameof(ChatRole.User))
        .Add(SampleKeys.CharlieKey, id: "charlie", nameof(ChatRole.User))
);

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

// ── CORS — allow React dev server ────────────────────────────────────────
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

namespace Trax.Samples.ChatService.Api
{
    public partial class Program;
}
