using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Extensions;
using Trax.Dashboard.Services.TrainDiscovery;
using Trax.Dashboard.Tests.Integration.Fakes;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class TrainBusIntegrationTests
{
    [Test]
    public void DiscoverTrains_WithEffectTrainBus_FindsAssemblyScannedTrains()
    {
        // Arrange — register trains via assembly scanning (as in a real app)
        var services = new ServiceCollection();

        services.AddTraxEffects(o =>
            o.AddServiceTrainBus(assemblies: [typeof(FakeTrainA).Assembly])
        );

        services.AddTraxDashboard();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var discoveryService = scope.ServiceProvider.GetRequiredService<ITrainDiscoveryService>();

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert — fake trains from this test assembly should be discovered
        result.Should().Contain(r => r.InputType == typeof(FakeInputA));
        result.Should().Contain(r => r.InputType == typeof(FakeInputB));
        result.Should().Contain(r => r.InputType == typeof(FakeInputC));
        result.Should().Contain(r => r.InputType == typeof(List<string>));
    }
}
