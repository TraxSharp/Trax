using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

/// <summary>
/// The registration contract a host depends on when it mounts the dashboard.
///
/// <para>The dashboard registers no train discovery of its own, so it relies on the host
/// having built a Trax pipeline. The precondition checks the TraxMarker that AddTrax()
/// registers, which catches a host that never called AddTrax() at all. It does not reach
/// discovery itself: ITrainDiscoveryService comes from AddMediator(), so AddTrax() with only
/// AddEffects() passes this check and then fails at render time, because the Trains page and
/// the metadata detail page both take it as a required injection.</para>
///
/// <para>Enforces <c>docs/adr/0001-the-dashboard-mounts-into-the-host-app.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0001-the-dashboard-mounts-into-the-host-app.md")]
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
        options!
            .RoutePrefix.Should()
            .Be(
                "/trax",
                "the dashboard mounts inside the host's app at a route prefix, rather than "
                    + "running as its own deployable. See "
                    + "docs/adr/0001-the-dashboard-mounts-into-the-host-app.md."
            );
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

        // Dashboard alone should NOT provide these: they come from Mediator
        provider.GetService<IServiceCollection>().Should().BeNull();
        provider.GetService<ITrainDiscoveryService>().Should().BeNull();
    }

    [Test]
    public void AddTraxDashboard_WithoutTraxMarker_ThrowsHelpfulException()
    {
        var services = new ServiceCollection();

        Action act = () => services.AddTraxDashboard();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddTrax*")
            .WithMessage("*services.AddTrax*");
    }

    [Test]
    public void AddTraxDashboard_RegistersScopedServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();

        services.AddTraxDashboard();

        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(Trax.Dashboard.Services.LocalStorage.ILocalStorageService)
                && sd.Lifetime == ServiceLifetime.Scoped
            );
        services
            .Should()
            .Contain(sd =>
                sd.ServiceType == typeof(Trax.Dashboard.Services.ThemeState.IThemeStateService)
                && sd.Lifetime == ServiceLifetime.Scoped
            );
        services
            .Should()
            .Contain(sd =>
                sd.ServiceType
                    == typeof(Trax.Dashboard.Services.DashboardSettings.IDashboardSettingsService)
                && sd.Lifetime == ServiceLifetime.Scoped
            );
    }
}
