using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Samples.GraphQLClient.Requests;
using Trax.Samples.GraphQLClient.Requests.A_RawString;
using Trax.Samples.GraphQLClient.Requests.D_Typed;
using Trax.Samples.GraphQLClient.Requests.E_Resource;
using Trax.Samples.GraphQLClient.Schema;

// Single-process sample: hosts the GraphQL server, then makes outbound calls to itself
// through the client. Demonstrates that mode A (raw string), mode E (.graphql resource),
// and mode D (POCO-derived) all converge on the same response when pointed at the same
// real schema. Mode D also illustrates the Path attribute for querying through nested
// envelopes like Trax's own discover.{namespace} grouping.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PlayerStore>();
builder.Services.AddGraphQLServer().AddType<PlayerQuery>();
PlayerSchemaConfiguration.Configure(builder.Services.AddGraphQLServer());

builder.Services.AddRouting();

// 5312: the GraphQLClient sample's range is 5310-5319 (the Gateway's servers take 5310 and 5311).
const string url = "http://localhost:5312";

// The client calls this same host's /graphql endpoint. AssemblySchemaProvider uses the same
// PlayerSchemaConfiguration.Configure delegate the server runs, so the client validates queries
// against the exact schema the server will execute, and needs no introspection.
builder
    .Services.AddTraxGraphQLClient(new Uri($"{url}/graphql"))
    .UseAssemblySchema(PlayerSchemaConfiguration.Configure);

var app = builder.Build();
app.MapGraphQL("/graphql");
app.Urls.Add(url);

// StartAsync returns once Kestrel is listening, so the client below never races the server.
await app.StartAsync();

var executor = app.Services.GetRequiredService<IGraphQLClientExecutor>();

Console.WriteLine("Running three modes against the same player:");
Console.WriteLine();

var modeA = await executor.Run(new GetPlayerByRawStringRequest { Id = "player-1" });
Print("A: raw string ", modeA);

var modeE = await executor.Run(new GetPlayerByResourceRequest { Id = "player-1" });
Print("E: .graphql   ", modeE);

var modeD = await executor.Run(new GetPlayerByTypedRequest { Id = "player-1" });
PrintTyped("D: POCO-typed", modeD);

// Same player, fetched through the Trax-style discover.{namespace} envelope. The Path
// attribute on the request keeps the consumer in typed mode for nested schemas instead
// of forcing a raw-string fallback.
var modeDNested = await executor.Run(new LookupPlayerByNestedPathRequest { Id = "player-1" });
PrintTyped("D: nested path", modeDNested!);

Console.WriteLine();
var aeMatch =
    modeA.Id == modeE.Id
    && modeA.Name == modeE.Name
    && modeA.Level == modeE.Level
    && modeA.Rank == modeE.Rank
    && modeA.Inventory.SequenceEqual(modeE.Inventory);
var adMatch =
    modeA.Id == modeD.Id
    && modeA.Name == modeD.Name
    && modeA.Level == modeD.Level
    && modeA.Rank == modeD.Rank
    && modeA.Inventory.Count == modeD.Inventory.Count;
Console.WriteLine($"  A == E (raw vs resource) : {aeMatch}");
Console.WriteLine($"  A ~ D (raw vs typed)     : {adMatch}");

await app.StopAsync();

// The point of the sample is that the modes agree, so a disagreement fails the run.
if (aeMatch && adMatch)
    return 0;
Console.Error.WriteLine("The modes returned different players.");
return 1;

static void Print(string label, PlayerProfile p) =>
    Console.WriteLine(
        $"  {label} -> {p.Name} (level {p.Level}, rank {p.Rank}, {p.Inventory.Count} items)"
    );

static void PrintTyped(string label, TypedPlayerProfile p) =>
    Console.WriteLine(
        $"  {label} -> {p.Name} (level {p.Level}, rank {p.Rank}, {p.Inventory.Count} items)"
    );
