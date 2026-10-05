// ─────────────────────────────────────────────────────────────────────────────
// Trax Chat Service: GraphQL subscriptions over WebSocket
//
// A single-process chat server. Chat mutations are Trax trains; when one
// completes, ChatLifecycleHook publishes its output to a room-scoped topic, and
// every participant subscribed with onChatEvent(chatRoomId:) receives it over
// the socket.
//
// Every operation acts as the authenticated caller. The trains are
// [TraxAuthorize(Roles = "User")] and read the caller from TraxPrincipal; no
// input names the caller. Room history, sending, inviting and onChatEvent are
// for a room's participants only, and an invite names only who to add: the
// server reads their name from the directory built below.
//
// Authentication: fake API keys, registered only in Development (NO WARRANTY)
//   X-Api-Key: alice-key-do-not-use-in-production   -> TraxApiKey:alice
//   X-Api-Key: bob-key-do-not-use-in-production     -> TraxApiKey:bob
//   X-Api-Key: charlie-key-do-not-use-in-production -> TraxApiKey:charlie
// A WebSocket client sends the key in connection_init: { "apiKey": "<key>" }.
//
// Run (no Docker: Trax metadata and chat data are both SQLite files):
//   dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api
//   Nitro IDE: http://localhost:5210/trax/graphql
//
// Try it: see samples/ChatService/README.md.
// ─────────────────────────────────────────────────────────────────────────────

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

namespace Trax.Samples.ChatService.Api
{
    public partial class Program;
}
