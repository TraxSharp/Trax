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
// real schema.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PlayerStore>();
builder.Services.AddGraphQLServer().AddType<PlayerQuery>();
PlayerSchemaConfiguration.Configure(builder.Services.AddGraphQLServer());

builder.Services.AddRouting();

var app = builder.Build();
app.MapGraphQL("/graphql");

var port = 5099;
app.Urls.Add($"http://localhost:{port}");

_ = app.RunAsync();

await Task.Delay(500);

// Now build a client pointed at our own /graphql endpoint. AssemblySchemaProvider uses
// the same PlayerSchemaConfiguration.Configure delegate the server runs, so the client
// validates queries against the exact schema the server will execute.
var clientServices = new ServiceCollection();
clientServices
    .AddTraxGraphQLClient(new Uri($"http://localhost:{port}/graphql"))
    .UseAssemblySchema(PlayerSchemaConfiguration.Configure);

// Sample uses a separate DI container for the client to keep the example self-contained;
// production code would normally consume the executor through the host's container instead.
#pragma warning disable ASP0000
await using var clientProvider = clientServices.BuildServiceProvider();
#pragma warning restore ASP0000
var executor = clientProvider.GetRequiredService<IGraphQLClientExecutor>();

Console.WriteLine("Running three modes against the same player:");
Console.WriteLine();

var modeA = await executor.Run(new GetPlayerByRawStringRequest { Id = "player-1" });
Print("A: raw string ", modeA);

var modeE = await executor.Run(new GetPlayerByResourceRequest { Id = "player-1" });
Print("E: .graphql   ", modeE);

var modeD = await executor.Run(new GetPlayerByTypedRequest { Id = "player-1" });
PrintTyped("D: POCO-typed", modeD);

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
return 0;

static void Print(string label, PlayerProfile p) =>
    Console.WriteLine(
        $"  {label} -> {p.Name} (level {p.Level}, rank {p.Rank}, {p.Inventory.Count} items)"
    );

static void PrintTyped(string label, TypedPlayerProfile p) =>
    Console.WriteLine(
        $"  {label} -> {p.Name} (level {p.Level}, rank {p.Rank}, {p.Inventory.Count} items)"
    );
