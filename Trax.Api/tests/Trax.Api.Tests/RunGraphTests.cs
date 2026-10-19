using AwesomeAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using NSubstitute;
using Trax.Api.Services.Runs;
using Trax.Core.Monad;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.ChainVerification;

namespace Trax.Api.Tests;

/// <summary>
/// A run is drawn on its train's chain as the host declares it now, and a chain can change between
/// the run and the read without its node ids changing (a step moved, a track's contents changed).
/// The run records the hash of the chain it started under, and <c>operations.runGraph</c> says when
/// the current chain's hash differs, rather than drawing the run as if it had run that way.
/// </summary>
[TestFixture]
public class RunGraphTests
{
    private const string Train = "Acme.IShippingTrain";

    // The chain when the run ran: fetch, then ship.
    private static readonly ChainGraph Before = Graph(
        Node("Fetch#0", "Fetch"),
        Node("Ship#0", "Ship")
    );

    // The chain now: the same two steps, ship moved before fetch. Every node id still matches.
    private static readonly ChainGraph After = Graph(
        Node("Ship#0", "Ship"),
        Node("Fetch#0", "Fetch")
    );

    [Test]
    public async Task A_run_under_an_older_chain_says_its_graph_changed()
    {
        Before.Hash.Should().NotBe(After.Hash, "moving a step changes the chain's hash");
        var (factory, run) = await SeedAsync(Before.Hash);
        await using var db = await factory.CreateDbContextAsync(default);

        var graph = await RunGraphs.ReadAsync(db, GraphsServing(After), run, default);

        graph!.ChainChanged.Should().BeTrue("the run recorded a chain the host no longer declares");
        graph.Hash.Should().Be(After.Hash, "the graph is still the chain as declared now");
        graph.Nodes.Select(n => n.Id).Should().Equal("Ship#0", "Fetch#0");
        graph.UnmatchedSteps.Should().BeEmpty("every step's node id still names a node");
    }

    [Test]
    public async Task A_run_under_the_current_chain_says_nothing_changed()
    {
        var (factory, run) = await SeedAsync(After.Hash);
        await using var db = await factory.CreateDbContextAsync(default);

        var graph = await RunGraphs.ReadAsync(db, GraphsServing(After), run, default);

        graph!.ChainChanged.Should().BeFalse();
    }

    [Test]
    public async Task A_run_that_recorded_no_hash_has_nothing_to_compare()
    {
        var (factory, run) = await SeedAsync(null);
        await using var db = await factory.CreateDbContextAsync(default);

        var graph = await RunGraphs.ReadAsync(db, GraphsServing(After), run, default);

        graph!
            .ChainChanged.Should()
            .BeFalse("a run from before hashes were recorded cannot tell, and is not flagged");
    }

    [Test]
    public async Task A_run_whose_train_has_no_graph_here_is_not_flagged()
    {
        var (factory, run) = await SeedAsync(Before.Hash);
        await using var db = await factory.CreateDbContextAsync(default);

        var graph = await RunGraphs.ReadAsync(
            db,
            Substitute.For<ITrainChainGraphs>(),
            run,
            default
        );

        graph!.HasGraph.Should().BeFalse();
        graph.ChainChanged.Should().BeFalse("nothing is drawn, so nothing is drawn wrongly");
    }

    private static ITrainChainGraphs GraphsServing(ChainGraph graph)
    {
        var graphs = Substitute.For<ITrainChainGraphs>();
        graphs.Find(Train).Returns(graph);
        return graphs;
    }

    private static async Task<(IDataContextProviderFactory Factory, long Run)> SeedAsync(
        string? chainHash
    )
    {
        var factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());
        await using var db = await factory.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = Train,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.TrainState = TrainState.Completed;
        run.EndTime = DateTime.UtcNow;
        run.ChainHash = chainHash;
        await db.Track(run);
        await db.SaveChanges(default);

        var position = 0;
        foreach (var (node, name) in new[] { ("Fetch#0", "Fetch"), ("Ship#0", "Ship") })
            db.JunctionRuns.Add(
                new JunctionRun
                {
                    MetadataId = run.Id,
                    Position = position++,
                    Kind = JunctionRunKind.Junction,
                    Name = name,
                    State = JunctionRunState.Completed,
                    StartedAt = DateTime.UtcNow,
                    NodeId = node,
                }
            );
        await db.SaveChanges(default);
        return (factory, run.Id);
    }

    private static ChainGraph Graph(params ChainGraphNode[] nodes) =>
        new("Acme.ShippingTrain", "Order", "Order", nodes, []);

    private static ChainGraphNode Node(string id, string junction) =>
        new(id, ChainStepKind.Chain, junction, "Order", "Order", false, []);
}
