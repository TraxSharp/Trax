using System.Globalization;
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
using Trax.Core.Extensions;
using Trax.Core.Monad;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
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
/// <c>operations.trains { chain, asksDecisions }</c>: a train's declared chain, read without a
/// run of it, in the shape <c>runGraph</c> draws a run in, and whether it asks a decider.
/// </summary>
[TestFixture]
public class TrainChainOperationsTests
{
    private const string Key = "train-chain-admin-key-0123456789abcdefgh";

    private IHost? _host;
    private IDataContextProviderFactory? _factory;

    private static readonly string Gated = typeof(IGatedChainTrain).FullName.NotNull();
    private static readonly string Plain = typeof(IPlainChainTrain).FullName.NotNull();
    private static readonly string Unread = typeof(IUnreadChainTrain).FullName.NotNull();

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
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
                        discovery
                            .DiscoverTrains()
                            .Returns([
                                Registration(typeof(IGatedChainTrain)),
                                Registration(typeof(IPlainChainTrain)),
                                Registration(typeof(IUnreadChainTrain)),
                            ]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddTraxGraphQL(graphql =>
                            graphql.ExposeOperationQueries().GateOperations(roles: "Admin")
                        );

                        var graphs = Substitute.For<ITrainChainGraphs>();
                        graphs.Find(Gated).Returns(GatedGraph());
                        graphs.Find(Plain).Returns(PlainGraph());
                        graphs.Find(Unread).Returns((ChainGraph?)null);
                        services.AddSingleton(graphs);
                        services.AddSingleton(_factory.NotNull());
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

        await _host.NotNull().StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _host.NotNull().StopAsync();
        _host?.Dispose();
    }

    [Test]
    public async Task Trains_Chain_OfATrainThatNeverRan_IsTheGraphARunOfItIsDrawnOn_AllNotReached()
    {
        var train = await TrainAsync(Gated);
        var chain = train.GetProperty("chain");

        chain.GetProperty("hasGraph").GetBoolean().Should().BeTrue();
        chain.GetProperty("metadataId").GetInt64().Should().Be(0);
        chain.GetProperty("unmatchedSteps").GetArrayLength().Should().Be(0);
        var nodes = chain.GetProperty("allNodes").EnumerateArray().ToList();
        nodes
            .Select(n => n.GetProperty("state").GetString())
            .Should()
            .AllBe("NOT_REACHED", "nothing has run: every declared node is still to reach");
        nodes.Should().AllSatisfy(n => n.GetProperty("steps").GetArrayLength().Should().Be(0));

        // The same nodes, in the same order and places, as runGraph draws a run of the train on.
        var run = await SeedRunAsync(Gated);
        var drawn = Operations(
            await PostAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{{ operations {{ runGraph(metadataId: {run}) {{ allNodes {{ id kind parentId track depth }} }} }} }}"
                )
            ),
            "runGraph"
        );
        Shape(nodes).Should().Equal(Shape(drawn.GetProperty("allNodes").EnumerateArray()));
    }

    [Test]
    public async Task Trains_Chain_StepsThatRecordNothing_AreNotReachedToo()
    {
        // A run would show the Resolve step as NOT_RECORDED; with no run there is nothing to say.
        var chain = (await TrainAsync(Plain)).GetProperty("chain");

        chain
            .GetProperty("allNodes")
            .EnumerateArray()
            .Select(n => (n.GetProperty("kind").GetString(), n.GetProperty("state").GetString()))
            .Should()
            .Equal(("CHAIN", "NOT_REACHED"), ("RESOLVE", "NOT_REACHED"));
    }

    [Test]
    public async Task Trains_AsksDecisions_IsTrueForATrainWithAGate_AndFalseWithout()
    {
        (await TrainAsync(Gated)).GetProperty("asksDecisions").GetBoolean().Should().BeTrue();
        (await TrainAsync(Plain)).GetProperty("asksDecisions").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task Trains_Chain_OfATrainWhoseChainCannotBeRead_HasNoGraph()
    {
        var train = await TrainAsync(Unread);

        train.GetProperty("chain").GetProperty("hasGraph").GetBoolean().Should().BeFalse();
        train.GetProperty("chain").GetProperty("allNodes").GetArrayLength().Should().Be(0);
        train.GetProperty("asksDecisions").GetBoolean().Should().BeFalse();
    }

    private async Task<JsonElement> TrainAsync(string fullName)
    {
        var trains = Operations(
            await PostAsync(
                    "{ operations { trains { fullName asksDecisions chain { metadataId train hasGraph "
                        + "unmatchedSteps { position } allNodes { id kind state parentId track depth "
                        + "steps { position } } } } } }"
                )
                .ConfigureAwait(false),
            "trains"
        );
        var train = trains
            .EnumerateArray()
            .Single(t =>
                string.Equals(
                    t.GetProperty("fullName").GetString(),
                    fullName,
                    StringComparison.Ordinal
                )
            );
        train.GetProperty("chain").GetProperty("train").GetString().Should().Be(fullName);
        return train;
    }

    private static List<string> Shape(IEnumerable<JsonElement> nodes) =>
        nodes
            .Select(n =>
                $"{n.GetProperty("id").GetString()}|{n.GetProperty("kind").GetString()}|"
                + string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Str(n, "parentId")}|{Str(n, "track")}|{n.GetProperty("depth").GetInt32()}"
                )
            )
            .ToList();

    private static string? Str(JsonElement e, string name) =>
        e.GetProperty(name).ValueKind == JsonValueKind.Null
            ? null
            : e.GetProperty(name).GetString();

    private static ChainGraph GatedGraph() =>
        new(
            Gated,
            "Order",
            "Order",
            [
                Node("Fetch#0", ChainStepKind.Chain, "Fetch"),
                Node("Decide<IsLarge>#0", ChainStepKind.Decide, "Decider"),
                new ChainGraphNode(
                    "Gate<IsLarge>#0",
                    ChainStepKind.Gate,
                    Junction: null,
                    "YesNoDecision<IsLarge>",
                    Out: null,
                    Opaque: false,
                    [
                        new ChainGraphTrack(
                            "Yes",
                            "It is large",
                            IsFallback: false,
                            [Node("Gate<IsLarge>#0/Yes/Review#0", ChainStepKind.Chain, "Review")]
                        ),
                        new ChainGraphTrack(
                            "No",
                            Description: null,
                            IsFallback: false,
                            [Node("Gate<IsLarge>#0/No/Ship#0", ChainStepKind.Chain, "Ship")]
                        ),
                    ]
                ),
            ],
            []
        );

    private static ChainGraph PlainGraph() =>
        new(
            Plain,
            "Order",
            "Order",
            [
                Node("Fetch#0", ChainStepKind.Chain, "Fetch"),
                Node("Resolve#0", ChainStepKind.Resolve, junction: null),
            ],
            []
        );

    private static ChainGraphNode Node(string id, ChainStepKind kind, string? junction) =>
        new(id, kind, junction, "Order", "Order", Opaque: false, []);

    private static TrainRegistration Registration(Type service) =>
        new()
        {
            ServiceType = service,
            ImplementationType = service,
            InputType = typeof(ChainInput),
            OutputType = typeof(ChainInput),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = service.Name,
            ImplementationTypeName = service.Name,
            InputTypeName = nameof(ChainInput),
            OutputTypeName = nameof(ChainInput),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

    private async Task<long> SeedRunAsync(string train)
    {
        var db = await _factory.NotNull().CreateDbContextAsync(default).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var run = Metadata.Create(
                new CreateMetadata
                {
                    Name = train,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                },
                TimeProvider.System
            );
            run.TrainState = TrainState.Completed;
            run.EndTime = TimeProvider.System.GetUtcNow().UtcDateTime;
            await db.Track(run).ConfigureAwait(false);
            await db.SaveChanges(default).ConfigureAwait(false);
            return run.Id;
        }
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
        var client = _host.NotNull().GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        request.Headers.Add("X-Api-Key", Key);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }
}
