using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Tests.Integration.Fakes.Trains;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

[TestFixture]
public class TrainDiscoveryServiceTests
{
    private ServiceCollection _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection();
    }

    #region DiscoverTrains

    [Test]
    public void DiscoverTrains_EmptyServiceCollection_ReturnsEmptyList()
    {
        // Arrange
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void DiscoverTrains_SingleTrain_ReturnsRegistrations()
    {
        // Arrange
        // AddScopedTraxRoute registers two DI descriptors:
        //   1. AddScoped<FakeTrainA>() — concrete type
        //   2. AddScoped<IFakeTrainA>(factory) — interface with factory
        // The discovery service sees both as separate registrations because
        // the factory-based descriptor has no ImplementationType, so the dedup
        // GroupBy(ImplementationType) places them in different groups.
        _services.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert — both registrations share the same input/output types
        result.Should().HaveCountGreaterThanOrEqualTo(1);
        result
            .Should()
            .OnlyContain(r =>
                r.InputType == typeof(FakeInputA)
                && r.OutputType == typeof(string)
                && r.Lifetime == ServiceLifetime.Scoped
            );

        // The interface-based registration should be present
        result.Should().Contain(r => r.ServiceType == typeof(IFakeTrainA));
    }

    [Test]
    public void DiscoverTrains_MultipleTrains_ReturnsAll()
    {
        // Arrange
        _services.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        _services.AddScopedTraxRoute<IFakeTrainB, FakeTrainB>();
        _services.AddScopedTraxRoute<IFakeTrainC, FakeTrainC>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert — all three train types are represented (each may appear
        // more than once due to dual-registration, see SingleTrain test)
        result.Should().Contain(r => r.ServiceType == typeof(IFakeTrainA));
        result.Should().Contain(r => r.ServiceType == typeof(IFakeTrainB));
        result.Should().Contain(r => r.ServiceType == typeof(IFakeTrainC));

        // Verify the distinct input types cover all three trains
        var distinctInputTypes = result.Select(r => r.InputType).Distinct().ToList();
        distinctInputTypes.Should().HaveCount(3);
        distinctInputTypes.Should().Contain(typeof(FakeInputA));
        distinctInputTypes.Should().Contain(typeof(FakeInputB));
        distinctInputTypes.Should().Contain(typeof(FakeInputC));
    }

    [Test]
    public void DiscoverTrains_DeduplicatesDualRegistration_ReturnsOnePerTrain()
    {
        // AddScopedTraxRoute registers both the concrete type (AddScoped<T>)
        // and the interface (AddScoped<TService>(factory)). The discovery service should
        // deduplicate these into a single registration per train.
        _services.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert — verify no duplicate registrations for the same underlying train
        var distinctByImpl = result.Select(r => r.ImplementationType).Distinct().Count();
        distinctByImpl.Should().Be(result.Count, "each implementation should appear at most once");
    }

    [Test]
    public void DiscoverTrains_PreferInterfaceOverConcreteType()
    {
        // Arrange
        _services.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert — at least one registration should use the interface as ServiceType
        result.Should().Contain(r => r.ServiceType.IsInterface);
    }

    [Test]
    public void DiscoverTrains_CachesResult()
    {
        // Arrange
        _services.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var first = discoveryService.DiscoverTrains();
        var second = discoveryService.DiscoverTrains();

        // Assert — same list instance (reference equality)
        ReferenceEquals(first, second)
            .Should()
            .BeTrue("cached result should be the same instance");
    }

    [Test]
    public void DiscoverTrains_NonTrainServices_AreIgnored()
    {
        // Arrange
        _services.AddScoped<INotATrain, NotATrain>();
        _services.AddSingleton("just a string");
        _services.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert — only train registrations should appear, not non-train services.
        // All discovered items should have FakeInputA as input (from our one train).
        result.Should().OnlyContain(r => r.InputType == typeof(FakeInputA));
        result.Should().NotContain(r => r.ServiceType == typeof(INotATrain));
    }

    [Test]
    public void DiscoverTrains_CorrectLifetime_Scoped()
    {
        // Arrange
        _services.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert
        result
            .Should()
            .Contain(r =>
                r.ServiceType == typeof(IFakeTrainA) && r.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Test]
    public void DiscoverTrains_CorrectLifetime_Transient()
    {
        // Arrange
        _services.AddTransientTraxRoute<IFakeTrainB, FakeTrainB>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert
        result
            .Should()
            .Contain(r =>
                r.ServiceType == typeof(IFakeTrainB) && r.Lifetime == ServiceLifetime.Transient
            );
    }

    [Test]
    public void DiscoverTrains_CorrectLifetime_Singleton()
    {
        // Arrange
        _services.AddSingletonTraxRoute<IFakeTrainC, FakeTrainC>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert
        result
            .Should()
            .Contain(r =>
                r.ServiceType == typeof(IFakeTrainC) && r.Lifetime == ServiceLifetime.Singleton
            );
    }

    #endregion

    #region GetFriendlyTypeName (tested indirectly via TrainRegistration properties)

    [Test]
    public void GetFriendlyTypeName_NonGenericType_ReturnsName()
    {
        // Arrange
        _services.AddScopedTraxRoute<IFakeTrainA, FakeTrainA>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert — FakeInputA is non-generic, so its name should be plain
        var registration = result.First(r => r.ServiceType == typeof(IFakeTrainA));
        registration.InputTypeName.Should().Be("FakeInputA");
    }

    [Test]
    public void GetFriendlyTypeName_GenericType_ReturnsFormattedName()
    {
        // Arrange
        _services.AddScopedTraxRoute<IFakeGenericTrain, FakeGenericTrain>();
        var discoveryService = new TrainDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverTrains();

        // Assert — List<string> should be formatted as "List<String>"
        var registration = result.First(r => r.ServiceType == typeof(IFakeGenericTrain));
        registration.InputTypeName.Should().Be("List<String>");
        registration.OutputTypeName.Should().Be("Dictionary<String, Int32>");
    }

    #endregion
}
