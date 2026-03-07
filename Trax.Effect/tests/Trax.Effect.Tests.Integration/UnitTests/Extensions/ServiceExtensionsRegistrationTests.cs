using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Route;
using Trax.Core.Step;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;

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

    #region CanonicalName — ServiceTrain

    [Test]
    public void AddScopedTraxRoute_ServiceTrain_SetsCanonicalNameToInterfaceFullName()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute<IFakeTrain, FakeTrain>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var train = scope.ServiceProvider.GetService<IFakeTrain>() as FakeTrain;

        train.Should().NotBeNull();
        train!.CanonicalName.Should().Be(typeof(IFakeTrain).FullName);
    }

    [Test]
    public void AddTransientTraxRoute_ServiceTrain_SetsCanonicalNameToInterfaceFullName()
    {
        var services = new ServiceCollection();
        services.AddTransientTraxRoute<IFakeTrain, FakeTrain>();
        using var provider = services.BuildServiceProvider();

        var train = provider.GetService<IFakeTrain>() as FakeTrain;

        train.Should().NotBeNull();
        train!.CanonicalName.Should().Be(typeof(IFakeTrain).FullName);
    }

    [Test]
    public void AddSingletonTraxRoute_ServiceTrain_SetsCanonicalNameToInterfaceFullName()
    {
        var services = new ServiceCollection();
        services.AddSingletonTraxRoute<IFakeTrain, FakeTrain>();
        using var provider = services.BuildServiceProvider();

        var train = provider.GetService<IFakeTrain>() as FakeTrain;

        train.Should().NotBeNull();
        train!.CanonicalName.Should().Be(typeof(IFakeTrain).FullName);
    }

    [Test]
    public void AddScopedTraxRoute_NonGeneric_ServiceTrain_SetsCanonicalNameToInterfaceFullName()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxRoute(typeof(IFakeTrain), typeof(FakeTrain));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var train = scope.ServiceProvider.GetService<IFakeTrain>() as FakeTrain;

        train.Should().NotBeNull();
        train!.CanonicalName.Should().Be(typeof(IFakeTrain).FullName);
    }

    [Test]
    public void AddScopedTraxStep_DoesNotSetCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxStep<IFakeStep, FakeStep>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var step = scope.ServiceProvider.GetService<IFakeStep>() as FakeStep;

        step.Should().NotBeNull();
        step!.GetType().GetProperty("CanonicalName").Should().BeNull();
    }

    #endregion

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

    public interface IFakeTrain : IServiceTrain<string, Unit> { }

    public class FakeTrain : ServiceTrain<string, Unit>, IFakeTrain
    {
        protected override Task<Either<Exception, Unit>> RunInternal(string input) =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    #endregion
}
