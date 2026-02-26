using Trax.Dashboard.Extensions;
using Trax.Dashboard.Services.WorkflowDiscovery;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Dashboard.Tests.Integration.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class WorkflowBusIntegrationTests
{
    [Test]
    public void DiscoverWorkflows_WithEffectWorkflowBus_FindsAssemblyScannedWorkflows()
    {
        // Arrange — register workflows via assembly scanning (as in a real app)
        var services = new ServiceCollection();

        services.AddTrax.CoreEffects(
            o => o.AddServiceTrainBus(assemblies: [typeof(FakeWorkflowA).Assembly])
        );

        services.AddTrax.CoreDashboard();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var discoveryService =
            scope.ServiceProvider.GetRequiredService<IWorkflowDiscoveryService>();

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert — fake workflows from this test assembly should be discovered
        result.Should().Contain(r => r.InputType == typeof(FakeInputA));
        result.Should().Contain(r => r.InputType == typeof(FakeInputB));
        result.Should().Contain(r => r.InputType == typeof(FakeInputC));
        result.Should().Contain(r => r.InputType == typeof(List<string>));
    }
}
