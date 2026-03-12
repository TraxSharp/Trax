using FluentAssertions;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Tests for <see cref="SubmitterRouting"/> — the public fluent API
/// for specifying which trains use a particular job submitter.
/// </summary>
[TestFixture]
public class SubmitterRoutingTests
{
    #region Test Types

    private interface ITrainA { }

    private interface ITrainB { }

    private interface ITrainC { }

    #endregion

    [Test]
    public void ForTrain_AddsTrainFullName()
    {
        var routing = new SubmitterRouting();

        routing.ForTrain<ITrainA>();

        routing.TrainNames.Should().ContainSingle().Which.Should().Be(typeof(ITrainA).FullName);
    }

    [Test]
    public void ForTrain_MultipleTrains_AddsAll()
    {
        var routing = new SubmitterRouting();

        routing.ForTrain<ITrainA>().ForTrain<ITrainB>().ForTrain<ITrainC>();

        routing.TrainNames.Should().HaveCount(3);
        routing.TrainNames.Should().Contain(typeof(ITrainA).FullName!);
        routing.TrainNames.Should().Contain(typeof(ITrainB).FullName!);
        routing.TrainNames.Should().Contain(typeof(ITrainC).FullName!);
    }

    [Test]
    public void ForTrain_DuplicateTrain_DeduplicatesViaHashSet()
    {
        var routing = new SubmitterRouting();

        routing.ForTrain<ITrainA>().ForTrain<ITrainA>();

        routing.TrainNames.Should().ContainSingle();
    }

    [Test]
    public void ForTrain_ReturnsThisForChaining()
    {
        var routing = new SubmitterRouting();

        var result = routing.ForTrain<ITrainA>();

        result.Should().BeSameAs(routing);
    }

    [Test]
    public void EmptyRouting_HasNoTrainNames()
    {
        var routing = new SubmitterRouting();

        routing.TrainNames.Should().BeEmpty();
    }
}
