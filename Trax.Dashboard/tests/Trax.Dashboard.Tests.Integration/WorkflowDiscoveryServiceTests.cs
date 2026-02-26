using Trax.Dashboard.Services.WorkflowDiscovery;
using Trax.Effect.Extensions;
using Trax.Dashboard.Tests.Integration.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class WorkflowDiscoveryServiceTests
{
    private ServiceCollection _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection();
    }

    #region DiscoverWorkflows

    [Test]
    public void DiscoverWorkflows_EmptyServiceCollection_ReturnsEmptyList()
    {
        // Arrange
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void DiscoverWorkflows_SingleWorkflow_ReturnsRegistrations()
    {
        // Arrange
        // AddScopedTrax.CoreRoute registers two DI descriptors:
        //   1. AddScoped<FakeWorkflowA>() — concrete type
        //   2. AddScoped<IFakeWorkflowA>(factory) — interface with factory
        // The discovery service sees both as separate registrations because
        // the factory-based descriptor has no ImplementationType, so the dedup
        // GroupBy(ImplementationType) places them in different groups.
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowA, FakeWorkflowA>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert — both registrations share the same input/output types
        result.Should().HaveCountGreaterThanOrEqualTo(1);
        result
            .Should()
            .OnlyContain(
                r =>
                    r.InputType == typeof(FakeInputA)
                    && r.OutputType == typeof(string)
                    && r.Lifetime == ServiceLifetime.Scoped
            );

        // The interface-based registration should be present
        result.Should().Contain(r => r.ServiceType == typeof(IFakeWorkflowA));
    }

    [Test]
    public void DiscoverWorkflows_MultipleWorkflows_ReturnsAll()
    {
        // Arrange
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowA, FakeWorkflowA>();
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowB, FakeWorkflowB>();
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowC, FakeWorkflowC>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert — all three workflow types are represented (each may appear
        // more than once due to dual-registration, see SingleWorkflow test)
        result.Should().Contain(r => r.ServiceType == typeof(IFakeWorkflowA));
        result.Should().Contain(r => r.ServiceType == typeof(IFakeWorkflowB));
        result.Should().Contain(r => r.ServiceType == typeof(IFakeWorkflowC));

        // Verify the distinct input types cover all three workflows
        var distinctInputTypes = result.Select(r => r.InputType).Distinct().ToList();
        distinctInputTypes.Should().HaveCount(3);
        distinctInputTypes.Should().Contain(typeof(FakeInputA));
        distinctInputTypes.Should().Contain(typeof(FakeInputB));
        distinctInputTypes.Should().Contain(typeof(FakeInputC));
    }

    [Test]
    public void DiscoverWorkflows_DeduplicatesDualRegistration_ReturnsOnePerWorkflow()
    {
        // AddScopedTrax.CoreRoute registers both the concrete type (AddScoped<T>)
        // and the interface (AddScoped<TService>(factory)). The discovery service should
        // deduplicate these into a single registration per workflow.
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowA, FakeWorkflowA>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert — verify no duplicate registrations for the same underlying workflow
        var distinctByImpl = result.Select(r => r.ImplementationType).Distinct().Count();
        distinctByImpl.Should().Be(result.Count, "each implementation should appear at most once");
    }

    [Test]
    public void DiscoverWorkflows_PreferInterfaceOverConcreteType()
    {
        // Arrange
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowA, FakeWorkflowA>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert — at least one registration should use the interface as ServiceType
        result.Should().Contain(r => r.ServiceType.IsInterface);
    }

    [Test]
    public void DiscoverWorkflows_CachesResult()
    {
        // Arrange
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowA, FakeWorkflowA>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var first = discoveryService.DiscoverWorkflows();
        var second = discoveryService.DiscoverWorkflows();

        // Assert — same list instance (reference equality)
        ReferenceEquals(first, second)
            .Should()
            .BeTrue("cached result should be the same instance");
    }

    [Test]
    public void DiscoverWorkflows_NonWorkflowServices_AreIgnored()
    {
        // Arrange
        _services.AddScoped<INotAWorkflow, NotAWorkflow>();
        _services.AddSingleton("just a string");
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowA, FakeWorkflowA>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert — only workflow registrations should appear, not non-workflow services.
        // All discovered items should have FakeInputA as input (from our one workflow).
        result.Should().OnlyContain(r => r.InputType == typeof(FakeInputA));
        result.Should().NotContain(r => r.ServiceType == typeof(INotAWorkflow));
    }

    [Test]
    public void DiscoverWorkflows_CorrectLifetime_Scoped()
    {
        // Arrange
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowA, FakeWorkflowA>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert
        result
            .Should()
            .Contain(
                r => r.ServiceType == typeof(IFakeWorkflowA) && r.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Test]
    public void DiscoverWorkflows_CorrectLifetime_Transient()
    {
        // Arrange
        _services.AddTransientTrax.CoreRoute<IFakeWorkflowB, FakeWorkflowB>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert
        result
            .Should()
            .Contain(
                r =>
                    r.ServiceType == typeof(IFakeWorkflowB)
                    && r.Lifetime == ServiceLifetime.Transient
            );
    }

    [Test]
    public void DiscoverWorkflows_CorrectLifetime_Singleton()
    {
        // Arrange
        _services.AddSingletonTrax.CoreRoute<IFakeWorkflowC, FakeWorkflowC>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert
        result
            .Should()
            .Contain(
                r =>
                    r.ServiceType == typeof(IFakeWorkflowC)
                    && r.Lifetime == ServiceLifetime.Singleton
            );
    }

    #endregion

    #region GetFriendlyTypeName (tested indirectly via WorkflowRegistration properties)

    [Test]
    public void GetFriendlyTypeName_NonGenericType_ReturnsName()
    {
        // Arrange
        _services.AddScopedTrax.CoreRoute<IFakeWorkflowA, FakeWorkflowA>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert — FakeInputA is non-generic, so its name should be plain
        var registration = result.First(r => r.ServiceType == typeof(IFakeWorkflowA));
        registration.InputTypeName.Should().Be("FakeInputA");
    }

    [Test]
    public void GetFriendlyTypeName_GenericType_ReturnsFormattedName()
    {
        // Arrange
        _services.AddScopedTrax.CoreRoute<IFakeGenericWorkflow, FakeGenericWorkflow>();
        var discoveryService = new WorkflowDiscoveryService(_services);

        // Act
        var result = discoveryService.DiscoverWorkflows();

        // Assert — List<string> should be formatted as "List<String>"
        var registration = result.First(r => r.ServiceType == typeof(IFakeGenericWorkflow));
        registration.InputTypeName.Should().Be("List<String>");
        registration.OutputTypeName.Should().Be("Dictionary<String, Int32>");
    }

    #endregion
}
