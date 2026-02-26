using Trax.Dashboard.Configuration;
using Trax.Dashboard.Extensions;
using Trax.Dashboard.Services.WorkflowDiscovery;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class DashboardServiceExtensionsTests
{
    [Test]
    public void AddTrax.CoreDashboard_RegistersDashboardOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax.CoreDashboard();
        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<DashboardOptions>();
        options.Should().NotBeNull();
        options!.RoutePrefix.Should().Be("/chainsharp");
        options.Title.Should().Be("Trax.Core");
    }

    [Test]
    public void AddTrax.CoreDashboard_WithConfigure_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax.CoreDashboard(o =>
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
    public void AddTrax.CoreDashboard_RegistersIServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax.CoreDashboard();
        using var provider = services.BuildServiceProvider();

        // Assert — WorkflowDiscoveryService depends on IServiceCollection being resolvable
        var serviceCollection = provider.GetService<IServiceCollection>();
        serviceCollection.Should().NotBeNull();
        serviceCollection.Should().BeSameAs(services);
    }

    [Test]
    public void AddTrax.CoreDashboard_RegistersWorkflowDiscoveryService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax.CoreDashboard();
        using var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var discoveryService = scope.ServiceProvider.GetService<IWorkflowDiscoveryService>();
        discoveryService.Should().NotBeNull();
        discoveryService.Should().BeOfType<WorkflowDiscoveryService>();
    }
}
