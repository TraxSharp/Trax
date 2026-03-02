using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Route;
using Trax.Core.Step;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Extensions;

[TestFixture]
public class ServiceExtensionsRegistrationTests
{
    [Test]
    public void AddScopedTraxStep_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScopedTraxStep<IFakeStep, FakeStep>();
        using var provider = services.BuildServiceProvider();

        // Act
        var step = provider.GetService<IFakeStep>();

        // Assert
        step.Should().NotBeNull();
        step.Should().BeOfType<FakeStep>();
    }

    [Test]
    public void AddTransientTraxStep_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransientTraxStep<IFakeStep, FakeStep>();
        using var provider = services.BuildServiceProvider();

        // Act
        var step = provider.GetService<IFakeStep>();

        // Assert
        step.Should().NotBeNull();
        step.Should().BeOfType<FakeStep>();
    }

    [Test]
    public void AddSingletonTraxStep_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingletonTraxStep<IFakeStep, FakeStep>();
        using var provider = services.BuildServiceProvider();

        // Act
        var step = provider.GetService<IFakeStep>();

        // Assert
        step.Should().NotBeNull();
        step.Should().BeOfType<FakeStep>();
    }

    [Test]
    public void AddScopedTraxRoute_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<IFakeRoute, FakeRoute>();
        using var provider = services.BuildServiceProvider();

        // Act
        var route = provider.GetService<IFakeRoute>();

        // Assert
        route.Should().NotBeNull();
        route.Should().BeOfType<FakeRoute>();
    }

    [Test]
    public void AddTransientTraxRoute_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransientTraxRoute<IFakeRoute, FakeRoute>();
        using var provider = services.BuildServiceProvider();

        // Act
        var route = provider.GetService<IFakeRoute>();

        // Assert
        route.Should().NotBeNull();
        route.Should().BeOfType<FakeRoute>();
    }

    [Test]
    public void AddSingletonTraxRoute_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingletonTraxRoute<IFakeRoute, FakeRoute>();
        using var provider = services.BuildServiceProvider();

        // Act
        var route = provider.GetService<IFakeRoute>();

        // Assert
        route.Should().NotBeNull();
        route.Should().BeOfType<FakeRoute>();
    }

    #region Test helpers

    public interface IFakeStep : IStep<string, int> { }

    public class FakeStep : Step<string, int>, IFakeStep
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    public interface IFakeRoute : IRoute<string, int> { }

    public class FakeRoute : IFakeRoute
    {
        public Task<int> Run(string input, CancellationToken cancellationToken = default) =>
            Task.FromResult(input.Length);
    }

    #endregion
}
