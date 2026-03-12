using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class TrainNameExpanderTests
{
    #region Test Types

    // Fake Type that reports a custom FullName, used to simulate interface vs concrete types
    private class FakeType(string fullName) : TypeDelegator(typeof(object))
    {
        public override string? FullName => fullName;
    }

    private class StubDiscoveryService(IReadOnlyList<TrainRegistration> registrations)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
    }

    private static TrainRegistration CreateRegistration(string serviceFullName, string implFullName)
    {
        return new TrainRegistration
        {
            ServiceType = new FakeType(serviceFullName),
            ImplementationType = new FakeType(implFullName),
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = serviceFullName,
            ImplementationTypeName = implFullName,
            InputTypeName = "Object",
            OutputTypeName = "Object",
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations = GraphQLOperation.Run,
        };
    }

    #endregion

    [Test]
    public void NameMatchesServiceType_AddsImplementationType()
    {
        var reg = CreateRegistration("Ns.IMyTrain", "Ns.MyTrain");
        var discovery = new StubDiscoveryService([reg]);

        var result = TrainNameExpander.ExpandTrainNames(["Ns.IMyTrain"], discovery);

        result.Should().Contain("Ns.IMyTrain");
        result.Should().Contain("Ns.MyTrain");
    }

    [Test]
    public void NameMatchesImplementationType_AddsServiceType()
    {
        var reg = CreateRegistration("Ns.IMyTrain", "Ns.MyTrain");
        var discovery = new StubDiscoveryService([reg]);

        var result = TrainNameExpander.ExpandTrainNames(["Ns.MyTrain"], discovery);

        result.Should().Contain("Ns.MyTrain");
        result.Should().Contain("Ns.IMyTrain");
    }

    [Test]
    public void NameMatchesNeither_KeepsOriginalOnly()
    {
        var reg = CreateRegistration("Ns.IMyTrain", "Ns.MyTrain");
        var discovery = new StubDiscoveryService([reg]);

        var result = TrainNameExpander.ExpandTrainNames(["Ns.SomeOtherTrain"], discovery);

        result.Should().ContainSingle().Which.Should().Be("Ns.SomeOtherTrain");
    }

    [Test]
    public void NullDiscoveryService_ReturnsOriginalNames()
    {
        var result = TrainNameExpander.ExpandTrainNames(["Ns.MyTrain", "Ns.OtherTrain"], null);

        result.Should().BeEquivalentTo(["Ns.MyTrain", "Ns.OtherTrain"]);
    }

    [Test]
    public void DuplicatesAreDeduplicated()
    {
        var reg = CreateRegistration("Ns.IMyTrain", "Ns.MyTrain");
        var discovery = new StubDiscoveryService([reg]);

        // Both names are already in the input
        var result = TrainNameExpander.ExpandTrainNames(["Ns.IMyTrain", "Ns.MyTrain"], discovery);

        result.Should().HaveCount(2);
        result.Should().Contain("Ns.IMyTrain");
        result.Should().Contain("Ns.MyTrain");
    }

    [Test]
    public void MultipleRegistrations_ExpandsAll()
    {
        var reg1 = CreateRegistration("Ns.ITrainA", "Ns.TrainA");
        var reg2 = CreateRegistration("Ns.ITrainB", "Ns.TrainB");
        var discovery = new StubDiscoveryService([reg1, reg2]);

        var result = TrainNameExpander.ExpandTrainNames(["Ns.ITrainA", "Ns.TrainB"], discovery);

        result.Should().HaveCount(4);
        result.Should().Contain("Ns.ITrainA");
        result.Should().Contain("Ns.TrainA");
        result.Should().Contain("Ns.ITrainB");
        result.Should().Contain("Ns.TrainB");
    }

    [Test]
    public void EmptyInputList_ReturnsEmpty()
    {
        var reg = CreateRegistration("Ns.IMyTrain", "Ns.MyTrain");
        var discovery = new StubDiscoveryService([reg]);

        var result = TrainNameExpander.ExpandTrainNames([], discovery);

        result.Should().BeEmpty();
    }
}
