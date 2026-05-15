using FluentAssertions;
using Trax.Effect.Broadcaster.SignalR.Configuration;
using Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Tests.Broadcaster.SignalR.Fakes.Trains;

namespace Trax.Effect.Tests.Broadcaster.SignalR.UnitTests;

[TestFixture]
public class SignalRSinkOptionsTests
{
    private static SignalRSinkOptions NewOptions() =>
        (SignalRSinkOptions)Activator.CreateInstance(typeof(SignalRSinkOptions), nonPublic: true)!;

    private static TrainLifecycleEventMessage Sample(
        string trainName = "Some.Other.IFoo",
        string eventType = "Completed"
    ) =>
        new(
            MetadataId: 1,
            ExternalId: "x",
            TrainName: trainName,
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: null,
            Output: null
        );

    [Test]
    public void Build_DefaultOptions_AllowsAllAndUsesDefaultProjection()
    {
        var config = NewOptions().Build();

        config.EventTypeFilter.Should().BeEmpty();
        config.TrainNameFilter.Should().BeEmpty();
        config.Matches(Sample()).Should().BeTrue();

        var projected = config.Projection(Sample(trainName: "X.IY"));
        projected.Should().BeOfType<TraxClientEvent>();
    }

    [Test]
    public void OnlyForEvents_Empty_Throws()
    {
        var act = () => NewOptions().OnlyForEvents();
        act.Should().Throw<ArgumentException>().WithMessage("*OnlyForEvents*at least one*");
    }

    [Test]
    public void OnlyForEvents_DuplicatesCollapsed()
    {
        var config = NewOptions().OnlyForEvents("Completed", "Completed").Build();
        config.EventTypeFilter.Should().BeEquivalentTo(new[] { "Completed" });
    }

    [Test]
    public void OnlyForEvents_NullArray_Throws()
    {
        var act = () => NewOptions().OnlyForEvents((string[])null!);
        act.Should().Throw<ArgumentException>().WithMessage("*OnlyForEvents*at least one*");
    }

    [Test]
    public void OnlyForTrains_NullTypeEntry_Throws()
    {
        var act = () => NewOptions().OnlyForTrains(typeof(ICheckGeocodeDriftTrain), null!);
        act.Should().Throw<ArgumentException>().WithMessage("*does not accept null*");
    }

    [Test]
    public void OnlyForEvents_NullOrWhitespace_Throws()
    {
        var act1 = () => NewOptions().OnlyForEvents("Completed", "");
        var act2 = () => NewOptions().OnlyForEvents("Completed", null!);
        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Test]
    public void OnlyForTrains_GenericOverload_StoresInterfaceFullName()
    {
        var config = NewOptions().OnlyForTrains<ICheckGeocodeDriftTrain>().Build();

        config
            .TrainNameFilter.Should()
            .BeEquivalentTo(new[] { typeof(ICheckGeocodeDriftTrain).FullName! });

        config.TrainNameFilter.Should().NotContain(typeof(ICheckGeocodeDriftTrain).Name);
    }

    [Test]
    public void OnlyForTrains_TwoAndThreeArgGenericOverloads_StoreAllFullNames()
    {
        var two = NewOptions().OnlyForTrains<ICheckGeocodeDriftTrain, IRunAuditTrain>().Build();
        two.TrainNameFilter.Should()
            .BeEquivalentTo(
                new[]
                {
                    typeof(ICheckGeocodeDriftTrain).FullName!,
                    typeof(IRunAuditTrain).FullName!,
                }
            );

        var three = NewOptions()
            .OnlyForTrains<ICheckGeocodeDriftTrain, IRunAuditTrain, IRefreshTilesTrain>()
            .Build();
        three
            .TrainNameFilter.Should()
            .BeEquivalentTo(
                new[]
                {
                    typeof(ICheckGeocodeDriftTrain).FullName!,
                    typeof(IRunAuditTrain).FullName!,
                    typeof(IRefreshTilesTrain).FullName!,
                }
            );
    }

    [Test]
    public void OnlyForTrains_NonInterfaceType_Throws()
    {
        var act = () => NewOptions().OnlyForTrains(typeof(string));
        act.Should().Throw<ArgumentException>().WithMessage("*expects train interface types*");
    }

    [Test]
    public void OnlyForTrains_NullArray_Throws()
    {
        var act = () => NewOptions().OnlyForTrains((Type[])null!);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void OnlyForTrains_EmptyArray_Throws()
    {
        var act = () => NewOptions().OnlyForTrains();
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void WithProjection_NullProjection_Throws()
    {
        var act = () => NewOptions().WithProjection<TraxClientEvent>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void WithProjection_CalledTwice_LastWins()
    {
        var config = NewOptions()
            .WithProjection(_ => new MyShapeA("A"))
            .WithProjection(_ => new MyShapeB("B"))
            .Build();

        var result = config.Projection(Sample());
        result.Should().BeOfType<MyShapeB>();
        ((MyShapeB)result).Label.Should().Be("B");
    }

    [Test]
    public void Matches_RespectsEventTypeFilter()
    {
        var config = NewOptions().OnlyForEvents("Completed").Build();
        config.Matches(Sample(eventType: "Completed")).Should().BeTrue();
        config.Matches(Sample(eventType: "Failed")).Should().BeFalse();
    }

    [Test]
    public void Matches_RespectsTrainNameFilter()
    {
        var config = NewOptions().OnlyForTrains<ICheckGeocodeDriftTrain>().Build();
        config
            .Matches(Sample(trainName: typeof(ICheckGeocodeDriftTrain).FullName!))
            .Should()
            .BeTrue();
        config.Matches(Sample(trainName: "Other.IFoo")).Should().BeFalse();
    }

    [Test]
    public void Matches_CombinesFiltersAsAnd()
    {
        var config = NewOptions()
            .OnlyForEvents("Completed")
            .OnlyForTrains<ICheckGeocodeDriftTrain>()
            .Build();

        var match = Sample(
            trainName: typeof(ICheckGeocodeDriftTrain).FullName!,
            eventType: "Completed"
        );
        var wrongTrain = Sample(trainName: "Other.IFoo", eventType: "Completed");
        var wrongEvent = Sample(
            trainName: typeof(ICheckGeocodeDriftTrain).FullName!,
            eventType: "Failed"
        );

        config.Matches(match).Should().BeTrue();
        config.Matches(wrongTrain).Should().BeFalse();
        config.Matches(wrongEvent).Should().BeFalse();
    }

    private record MyShapeA(string Label);

    private record MyShapeB(string Label);
}
