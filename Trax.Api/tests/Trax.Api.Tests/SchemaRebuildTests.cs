using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Tests.AuthE2E;
using Trax.Api.Tests.Fakes.Machines;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

[TestFixture]
public class SchemaRebuildTests
{
    [Test]
    public async Task A_second_provider_from_the_same_services_builds_the_same_schema()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        var reg = new ServiceCollection();
        reg.AddScoped<IEchoTrain, EchoTrain>();
        services.AddSingleton<ITrainDiscoveryService>(new TrainDiscoveryService(reg));
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddTraxGraphQL();

        await using var first = services.BuildServiceProvider();
        var a = await first.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync("trax");
        a.Schema.ToString().Should().Contain("AuditDiscoverQueries");

        await using var second = services.BuildServiceProvider();
        var act = async () =>
            await second.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync("trax");
        var b = (await act.Should().NotThrowAsync()).Subject;
        b.Schema.ToString().Should().Be(a.Schema.ToString());
    }

    /// <summary>
    /// A machine's persisted documents are generated from the schema and their ids hash the document text, so two
    /// providers (two nodes) must build the same <c>machines</c> schema, field for field and in the same order.
    /// </summary>
    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public async Task A_second_provider_builds_the_same_machines_schema()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(e => e.UseInMemory()).AddStateMachines());
        services.AddSingleton<IMachine, ShopMachine>();
        services.AddSingleton<IMachine, PartsMachine>();
        services.AddScoped<ISnapshotPrincipal>(_ => Substitute.For<ISnapshotPrincipal>());
        var reg = new ServiceCollection();
        reg.AddScoped<IEchoTrain, EchoTrain>();
        services.AddSingleton<ITrainDiscoveryService>(new TrainDiscoveryService(reg));
        services.AddTraxGraphQL();

        await using var first = services.BuildServiceProvider();
        var a = await first.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync("trax");
        a.Schema.ToString()
            .Should()
            .Contain(
                "machines",
                "the marked machines are served under the machines root, per "
                    + "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
            );

        await using var second = services.BuildServiceProvider();
        var b = await second
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        b.Schema.ToString()
            .Should()
            .Be(
                a.Schema.ToString(),
                "a persisted machine document's id hashes text generated from this schema"
            );
    }
}
