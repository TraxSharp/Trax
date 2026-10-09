using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Core.Monad;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.ChainVerification;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// <c>operations.runGraph</c> and <c>operations.declaredChain</c> over HTTP, for a chain nested
/// deeper than a GraphQL query can follow: the nested <c>tracks { nodes }</c> fields repeat, and the
/// server's field-cycle limit refuses a query that repeats them past a few levels. Both answer
/// the whole graph through <c>allNodes</c>, one flat list a client rebuilds the tree from by each
/// node's <c>parentId</c> and <c>track</c>. And a run that has ended shows a step whose end was
/// never recorded as <c>INTERRUPTED</c>, never as still in progress.
/// </summary>
[TestFixture]
public class RunGraphOverHttpTests
{
    private const string Key = "graph-admin-key";
    private const string Train = "Acme.IDeepTrain";

    // Six Parallel steps, each nested in a branch of the one before, with a junction at the bottom.
    private const int Levels = 6;

    private IHost _host = null!;
    private IDataContextProviderFactory _factory = null!;
    private ChainGraph _graph = null!;
    private List<string> _parallelIds = null!;
    private string _leafId = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        (_graph, _parallelIds, _leafId) = DeepGraph();
        _factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys => keys.Add(Key, id: "admin", "Admin"));
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddTraxGraphQL(graphql =>
                            graphql.ExposeOperationQueries().GateOperations(roles: "Admin")
                        );

                        var graphs = Substitute.For<ITrainChainGraphs>();
                        graphs.Find(Train).Returns(_graph);
                        services.AddSingleton(graphs);
                        services.AddSingleton(_factory);
                        services.AddScoped(_ => Substitute.For<IOperationsService>());
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        services.AddScoped(_ => Substitute.For<ITrainExecutionService>());
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await _host.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Test]
    public async Task ANestedQueryAsDeepAsTheChain_IsRefusedByTheCycleLimit()
    {
        // What a client following tracks { nodes } to the bottom of the chain would have to send.
        var selection = "id state";
        for (var i = 0; i < Levels; i++)
            selection = $"id state tracks {{ name nodes {{ {selection} }} }}";

        var run = await SeedRunAsync(TrainState.Completed, (_leafId, JunctionRunState.Completed));
        var doc = await PostAsync(
            $"{{ operations {{ runGraph(metadataId: {run}) {{ nodes {{ {selection} }} }} }} }}"
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeTrue(
                "the nested fields cannot reach the bottom of this chain: "
                    + doc.RootElement.GetRawText()
            );
    }

    [Test]
    public async Task RunGraph_AllNodes_ReachesEveryNode_WithWhereItSits()
    {
        var run = await SeedRunAsync(TrainState.Completed, (_leafId, JunctionRunState.Completed));

        var graph = Operations(
            await PostAsync(
                $"{{ operations {{ runGraph(metadataId: {run}) {{ "
                    + "allNodes { id kind state parentId track depth tracks { name taken } } } } }"
            ),
            "runGraph"
        );

        var all = graph.GetProperty("allNodes").EnumerateArray().ToList();
        all.Select(n => n.GetProperty("id").GetString())
            .Should()
            .Equal(DeclaredOrder(_graph.Nodes), "depth first, each node before its tracks' nodes");

        var leaf = all.Single(n => n.GetProperty("id").GetString() == _leafId);
        leaf.GetProperty("state").GetString().Should().Be("COMPLETED");
        leaf.GetProperty("depth").GetInt32().Should().Be(Levels);
        leaf.GetProperty("parentId").GetString().Should().Be(_parallelIds[^1]);
        leaf.GetProperty("track").GetString().Should().Be("deep");

        all[0].GetProperty("parentId").ValueKind.Should().Be(JsonValueKind.Null);
        all[0].GetProperty("track").ValueKind.Should().Be(JsonValueKind.Null);
        all[0].GetProperty("depth").GetInt32().Should().Be(0);

        // The tree the flat list rebuilds is the one the nested fields describe.
        Rebuild(all).Should().Be(Shape(_graph.Nodes));
    }

    [Test]
    public async Task DeclaredChain_AllNodes_ReachesEveryNode_WithWhereItSits()
    {
        var chain = Operations(
            await PostAsync(
                $"{{ operations {{ declaredChain(train: \"{Train}\") {{ "
                    + "allNodes { id kind parentId track depth tracks { name isFallback } } } } }"
            ),
            "declaredChain"
        );

        var all = chain.GetProperty("allNodes").EnumerateArray().ToList();
        all.Select(n => n.GetProperty("id").GetString())
            .Should()
            .Equal(DeclaredOrder(_graph.Nodes));
        var leaf = all.Single(n => n.GetProperty("id").GetString() == _leafId);
        leaf.GetProperty("depth").GetInt32().Should().Be(Levels);
        leaf.GetProperty("parentId").GetString().Should().Be(_parallelIds[^1]);
        Rebuild(all).Should().Be(Shape(_graph.Nodes));
    }

    [Test]
    public async Task AStepStillInProgress_InARunThatHasEnded_IsInterrupted()
    {
        // The leaf's end event was dropped: its row still says in progress, though the run ended.
        var run = await SeedRunAsync(TrainState.Completed, (_leafId, JunctionRunState.InProgress));

        var graph = Operations(
            await PostAsync(
                $"{{ operations {{ runGraph(metadataId: {run}) {{ allNodes {{ id state }} }} }} }}"
            ),
            "runGraph"
        );

        var states = graph
            .GetProperty("allNodes")
            .EnumerateArray()
            .ToDictionary(n => n.GetProperty("id").GetString()!, n => n.GetProperty("state"));
        states[_leafId].GetString().Should().Be("INTERRUPTED");
        states[_parallelIds[0]]
            .GetString()
            .Should()
            .Be("INTERRUPTED", "a step whose branch was interrupted is not still running");
    }

    [Test]
    public async Task AStepInProgress_InARunStillRunning_IsInProgress()
    {
        var run = await SeedRunAsync(TrainState.InProgress, (_leafId, JunctionRunState.InProgress));

        var graph = Operations(
            await PostAsync(
                $"{{ operations {{ runGraph(metadataId: {run}) {{ allNodes {{ id state }} }} }} }}"
            ),
            "runGraph"
        );

        graph
            .GetProperty("allNodes")
            .EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == _leafId)
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("IN_PROGRESS");
    }

    private static (ChainGraph Graph, List<string> Parallels, string Leaf) DeepGraph()
    {
        var parallels = new List<string>();
        var prefix = "";
        for (var i = 0; i < Levels; i++)
        {
            parallels.Add($"{prefix}Parallel#0");
            prefix = $"{prefix}Parallel#0/deep/";
        }
        var leaf = $"{prefix}Leaf#0";

        ChainGraphNode node = new(leaf, ChainStepKind.Chain, "Leaf", "Paper", "Paper", false, []);
        for (var i = Levels - 1; i >= 0; i--)
        {
            var id = parallels[i];
            node = new ChainGraphNode(
                id,
                ChainStepKind.Parallel,
                null,
                "Paper",
                "Paper",
                false,
                [
                    new ChainGraphTrack("deep", null, false, [node]),
                    new ChainGraphTrack(
                        "side",
                        null,
                        false,
                        [
                            new ChainGraphNode(
                                $"{id}/side/Side#0",
                                ChainStepKind.Chain,
                                "Side",
                                "Paper",
                                "Paper",
                                false,
                                []
                            ),
                        ]
                    ),
                ]
            );
        }

        var graph = new ChainGraph(
            "Acme.DeepTrain",
            "Paper",
            "Paper",
            [
                new ChainGraphNode(
                    "Fetch#0",
                    ChainStepKind.Chain,
                    "Fetch",
                    "Paper",
                    "Paper",
                    false,
                    []
                ),
                node,
            ],
            []
        );
        return (graph, parallels, leaf);
    }

    private static List<string> DeclaredOrder(IReadOnlyList<ChainGraphNode> nodes) =>
        nodes
            .SelectMany(n =>
                n.Tracks.SelectMany(t => DeclaredOrder(t.Nodes)).Prepend(n.Id).ToList()
            )
            .ToList();

    // A tree written out as "id[track(children) ...]", from the declared graph.
    private static string Shape(IReadOnlyList<ChainGraphNode> nodes) =>
        string.Join(
            " ",
            nodes.Select(n =>
                n.Id
                + "["
                + string.Join(" ", n.Tracks.Select(t => $"{t.Name}({Shape(t.Nodes)})"))
                + "]"
            )
        );

    // The same tree, rebuilt as a client would from the flat list's parentId and track, in list order.
    private static string Rebuild(List<JsonElement> all)
    {
        return Children(null, null);

        string Children(string? parent, string? track) =>
            string.Join(
                " ",
                all.Where(n => Str(n, "parentId") == parent && Str(n, "track") == track)
                    .Select(n =>
                    {
                        var id = Str(n, "id")!;
                        var tracks = n.GetProperty("tracks")
                            .EnumerateArray()
                            .Select(t => t.GetProperty("name").GetString()!)
                            .Select(name => $"{name}({Children(id, name)})");
                        return id + "[" + string.Join(" ", tracks) + "]";
                    })
            );

        static string? Str(JsonElement e, string name) =>
            e.GetProperty(name).ValueKind == JsonValueKind.Null
                ? null
                : e.GetProperty(name).GetString();
    }

    private async Task<long> SeedRunAsync(
        TrainState state,
        params (string NodeId, JunctionRunState State)[] steps
    )
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = Train,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.TrainState = state;
        if (state is TrainState.Completed or TrainState.Failed or TrainState.Cancelled)
            run.EndTime = DateTime.UtcNow;
        await db.Track(run);
        await db.SaveChanges(default);

        var position = 0;
        foreach (var step in steps)
            db.JunctionRuns.Add(
                new JunctionRun
                {
                    MetadataId = run.Id,
                    Position = position++,
                    Kind = JunctionRunKind.Junction,
                    Name = step.NodeId.Split('/')[^1],
                    State = step.State,
                    StartedAt = DateTime.UtcNow,
                    NodeId = step.NodeId,
                }
            );
        await db.SaveChanges(default);
        return run.Id;
    }

    private static JsonElement Operations(JsonDocument doc, string field)
    {
        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        return doc.RootElement.GetProperty("data").GetProperty("operations").GetProperty(field);
    }

    private async Task<JsonDocument> PostAsync(string query)
    {
        var client = _host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        request.Headers.Add("X-Api-Key", Key);
        var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
