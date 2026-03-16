using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Junction;
using Trax.Core.Route;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Extensions;

[TestFixture]
public class ServiceExtensionsRegistrationTests
{
    [Test]
    public void AddScopedTraxJunction_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScopedTraxJunction<IFakeJunction, FakeJunction>();
        using var provider = services.BuildServiceProvider();

        // Act
        var junction = provider.GetService<IFakeJunction>();

        // Assert
        junction.Should().NotBeNull();
        junction.Should().BeOfType<FakeJunction>();
    }

    [Test]
    public void AddTransientTraxJunction_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransientTraxJunction<IFakeJunction, FakeJunction>();
        using var provider = services.BuildServiceProvider();

        // Act
        var junction = provider.GetService<IFakeJunction>();

        // Assert
        junction.Should().NotBeNull();
        junction.Should().BeOfType<FakeJunction>();
    }

    [Test]
    public void AddSingletonTraxJunction_Resolves_ViaInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingletonTraxJunction<IFakeJunction, FakeJunction>();
        using var provider = services.BuildServiceProvider();

        // Act
        var junction = provider.GetService<IFakeJunction>();

        // Assert
        junction.Should().NotBeNull();
        junction.Should().BeOfType<FakeJunction>();
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
    public void AddScopedTraxJunction_DoesNotSetCanonicalName()
    {
        var services = new ServiceCollection();
        services.AddScopedTraxJunction<IFakeJunction, FakeJunction>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var junction = scope.ServiceProvider.GetService<IFakeJunction>() as FakeJunction;

        junction.Should().NotBeNull();
        junction!.GetType().GetProperty("CanonicalName").Should().BeNull();
    }

    #endregion

    #region Test helpers

    public interface IFakeJunction : IJunction<string, int> { }

    public class FakeJunction : Junction<string, int>, IFakeJunction
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
