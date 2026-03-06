using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class DashboardServiceExtensionsTests
{
    [Test]
    public void AddTraxDashboard_RegistersDashboardOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();

        // Act
        services.AddTraxDashboard();
        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<DashboardOptions>();
        options.Should().NotBeNull();
        options!.RoutePrefix.Should().Be("/trax");
        options.Title.Should().Be("Trax");
    }

    [Test]
    public void AddTraxDashboard_WithConfigure_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();

        // Act
        services.AddTraxDashboard(o =>
        {
            o.Title = "Custom Title";
            o.RoutePrefix = "/custom";
        });
        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<DashboardOptions>();
        options.Title.Should().Be("Custom Title");
        options.RoutePrefix.Should().Be("/custom");
    }

    [Test]
    public void AddTraxDashboard_DoesNotRegisterDiscoveryService()
    {
        // ITrainDiscoveryService and IServiceCollection are now registered by
        // Mediator's AddMediator(), not by AddTraxDashboard().
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();

        services.AddTraxDashboard();
        using var provider = services.BuildServiceProvider();

        // Dashboard alone should NOT provide these — they come from Mediator
        provider.GetService<IServiceCollection>().Should().BeNull();
        provider.GetService<ITrainDiscoveryService>().Should().BeNull();
    }
}
