using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.ChainVerification;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// The graph of each registered train, read once and looked up by the train's canonical name.
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md:
/// drawing a train reads its declaration and runs none of its junctions.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
[TestFixture]
public class TrainChainGraphsTests
{
    [Test]
    public async Task Find_ByCanonicalName_GivesTheTrainsGraph()
    {
        await using var provider = Provider(services =>
            services.AddScoped<IGraphedTrain, GraphedTrain>()
        );
        var graphs = Graphs(provider, Registration<IGraphedTrain, GraphedTrain>());

        var graph = graphs.Find(typeof(IGraphedTrain).FullName!);

        graph.Should().NotBeNull();
        graph!.Train.Should().Be(typeof(GraphedTrain).FullName);
        graph.Input.Should().Be(nameof(GraphInput));
        graph.Nodes.Select(n => n.Id).Should().Equal("TextToCount#0", "CountToFlag#0", "Resolve#0");
    }

    [Test]
    public async Task Find_ReadsEachChainOnce()
    {
        GraphedTrain.Declared = 0;
        await using var provider = Provider(services =>
            services.AddScoped<IGraphedTrain, GraphedTrain>()
        );
        var graphs = Graphs(provider, Registration<IGraphedTrain, GraphedTrain>());

        var first = graphs.Find(typeof(IGraphedTrain).FullName!);
        var second = graphs.Find(typeof(IGraphedTrain).FullName!);

        second.Should().BeSameAs(first);
        GraphedTrain.Declared.Should().Be(1, "a chain cannot change while the host runs");
    }

    [Test]
    public async Task FindDeclared_GivesTheChainItsGraphIsDrawnFrom_ReadOnce()
    {
        GraphedTrain.Declared = 0;
        await using var provider = Provider(services =>
            services.AddScoped<IGraphedTrain, GraphedTrain>()
        );
        var graphs = Graphs(provider, Registration<IGraphedTrain, GraphedTrain>());

        var declared = graphs.FindDeclared(typeof(IGraphedTrain).FullName!);
        var graph = graphs.Find(typeof(IGraphedTrain).FullName!);

        declared.Should().NotBeNull();
        declared!.Train.Should().Be(typeof(GraphedTrain));
        declared.Input.Should().Be(typeof(GraphInput));
        declared.Output.Should().Be(typeof(bool));
        ChainGraph
            .From(declared.Chain, declared.Train, declared.Input, declared.Output)
            .Hash.Should()
            .Be(graph!.Hash, "a resume is checked against the chain the graph is drawn from");
        GraphedTrain.Declared.Should().Be(1, "the graph and the chain are read together");
        graphs.FindDeclared("System.IO.File").Should().BeNull();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("System.IO.File")]
    [TestCase(
        "Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests.TrainChainGraphsTests+GraphedTrain"
    )]
    public async Task Find_RefusesAnythingButARegisteredTrainsCanonicalName(string name)
    {
        await using var provider = Provider(services =>
            services.AddScoped<IGraphedTrain, GraphedTrain>()
        );
        var graphs = Graphs(provider, Registration<IGraphedTrain, GraphedTrain>());

        graphs
            .Find(name)
            .Should()
            .BeNull(
                "only a registered train's canonical name is looked up, so no caller can make the "
                    + "host load or build a type it names"
            );
    }

    [Test]
    public async Task Find_GivesNull_WhenTheTrainCannotBeBuiltHere()
    {
        await using var provider = Provider(services =>
            services.AddScoped<IUnbuildableTrain, UnbuildableTrain>()
        );
        var graphs = Graphs(provider, Registration<IUnbuildableTrain, UnbuildableTrain>());

        graphs.Find(typeof(IUnbuildableTrain).FullName!).Should().BeNull();
    }

    [Test]
    public async Task AddMediator_RegistersTheGraphs()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator => mediator.ScanAssemblies(typeof(ITrainBus).Assembly))
        );

        await using var provider = services.BuildServiceProvider();

        provider.GetService<ITrainChainGraphs>().Should().NotBeNull();
    }

    private static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects));
        configure(services);
        return services.BuildServiceProvider();
    }

    private static TrainChainGraphs Graphs(
        IServiceProvider provider,
        TrainRegistration registration
    )
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([registration]);

        return new TrainChainGraphs(discovery, provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static TrainRegistration Registration<TService, TTrain>() =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(TTrain),
            InputType = typeof(GraphInput),
            OutputType = typeof(bool),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TTrain).Name,
            InputTypeName = nameof(GraphInput),
            OutputTypeName = nameof(Boolean),
            HasAllowAnonymousAttribute = true,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = 0,
        };

    public sealed record GraphInput(string Text);

    private sealed class TextToCount : Junction<GraphInput, int>
    {
        public override Task<int> Run(GraphInput input) => Task.FromResult(input.Text.Length);
    }

    private sealed class CountToFlag : Junction<int, bool>
    {
        public override Task<bool> Run(int input) => Task.FromResult(input > 0);
    }

    public interface IGraphedTrain : IServiceTrain<GraphInput, bool>;

    public class GraphedTrain : ServiceTrain<GraphInput, bool>, IGraphedTrain
    {
        public static int Declared;

        protected override Task<Either<Exception, bool>> Junctions()
        {
            Interlocked.Increment(ref Declared);
            return Chain<TextToCount>().Chain<CountToFlag>().Resolve();
        }
    }

    public interface IUnbuildableTrain : IServiceTrain<GraphInput, bool>;

    public sealed class Missing;

    public class UnbuildableTrain(Missing missing)
        : ServiceTrain<GraphInput, bool>,
            IUnbuildableTrain
    {
        public Missing Needs { get; } = missing;

        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<TextToCount>().Chain<CountToFlag>().Resolve();
    }
}
