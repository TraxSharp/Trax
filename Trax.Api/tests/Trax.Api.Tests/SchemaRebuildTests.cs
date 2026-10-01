using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Tests.AuthE2E;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
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
}
