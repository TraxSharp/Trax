using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Extensions;
using Trax.Dashboard.Services.TrainDiscovery;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class DashboardServiceExtensionsTests
{
    [Test]
    public void AddTraxDashboard_RegistersDashboardOptions()
    {
        // Arrange
        var services = new ServiceCollection();

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
    public void AddTraxDashboard_RegistersIServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTraxDashboard();
        using var provider = services.BuildServiceProvider();

        // Assert — TrainDiscoveryService depends on IServiceCollection being resolvable
        var serviceCollection = provider.GetService<IServiceCollection>();
        serviceCollection.Should().NotBeNull();
        serviceCollection.Should().BeSameAs(services);
    }

    [Test]
    public void AddTraxDashboard_RegistersTrainDiscoveryService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTraxDashboard();
        using var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var discoveryService = scope.ServiceProvider.GetService<ITrainDiscoveryService>();
        discoveryService.Should().NotBeNull();
        discoveryService.Should().BeOfType<TrainDiscoveryService>();
    }
}
